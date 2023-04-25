using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.VirtualFileSystem.Matcher;
using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem {
    public Task Search(
        IVirtualFolder irootFolder,
        string query,
        Action<IVirtualFileSystem.SearchProgress> progressCallback,
        Action<IVirtualFolder> folderFoundCallback,
        Action<IVirtualFile> fileFoundCallback,
        int numThreads = default,
        TimeSpan timeoutPerEntry = default,
        CancellationToken cancellationToken = default) => Task.Factory.StartNew(async () => {

        var rootFolder = (VirtualFolder) irootFolder;

        cancellationToken.ThrowIfCancellationRequested();

        if (new QueryTokenizer(query).Parse() is not { } matcher)
            return;

        var stopwatches = ObjectPool.Create(new DefaultPooledObjectPolicy<Stopwatch>());

        Debug.Print(matcher.ToString());

        if (numThreads == default)
            numThreads = Environment.ProcessorCount;
        if (timeoutPerEntry == default)
            timeoutPerEntry = TimeSpan.FromMilliseconds(500);

        cancellationToken.ThrowIfCancellationRequested();

        var activeTasks = new HashSet<Task>();
        var queue = System.Threading.Channels.Channel.CreateUnbounded<object?>();

        var progress = new IVirtualFileSystem.SearchProgress(rootFolder);

        async Task Traverse(VirtualFolder folder) {
            cancellationToken.ThrowIfCancellationRequested();

            await AsFoldersResolved(folder);
            var folders = GetFolders(folder);
            progress.Total += folders.Count + folder.Files.Count;

            cancellationToken.ThrowIfCancellationRequested();

            var asFileNamesResolved = AsFileNamesResolved(folder);
            await queue.Writer.WriteAsync(folders, cancellationToken).ConfigureAwait(false);
            foreach (var f in folders)
                await Traverse(f);
            await queue.Writer.WriteAsync(GetFiles(await asFileNamesResolved), cancellationToken).ConfigureAwait(false);
        }

        long nextProgressReportedMilliseconds = 0;
        progress.Stopwatch.Start();

        _ = Task.Run(async () => {
            await queue.Writer.WriteAsync(new object[] {rootFolder}, cancellationToken);
            await Traverse(rootFolder);
            await queue.Writer.WriteAsync(null, cancellationToken);
        }, cancellationToken);

        var itemList = new List<object>();
        while (true) {
            while (activeTasks.Count >= numThreads) {
                await Task.WhenAny(activeTasks);
                activeTasks.RemoveWhere(x => x.IsCompleted);
            }

            var @object = await queue.Reader.ReadAsync(cancellationToken);
            if (@object is null)
                break;

            progress.LastObject = @object;
            if (progress.Stopwatch.ElapsedMilliseconds >= nextProgressReportedMilliseconds) {
                progressCallback(progress);
                nextProgressReportedMilliseconds = progress.Stopwatch.ElapsedMilliseconds + 200;
            }

            itemList.Clear();
            switch (@object) {
                case List<IVirtualFolder> folders:
                    itemList.AddRange(folders);
                    break;
                case List<IVirtualFile> files:
                    itemList.AddRange(files);
                    break;
            }

            foreach (var item in itemList) {
                while (activeTasks.Count >= numThreads) {
                    await Task.WhenAny(activeTasks);
                    activeTasks.RemoveWhere(x => x.IsCompleted);
                }

                var task = Task.Run(async () => {
                        var stopwatch = stopwatches.Get();
                        try {
                            switch (item) {
                                case VirtualFolder folder:
                                    if (await matcher.Matches(this, folder, stopwatch, timeoutPerEntry,
                                            cancellationToken))
                                        return () => folderFoundCallback(folder);
                                    else
                                        return null;
                                case VirtualFile file:
                                    var lookup = new Lazy<IVirtualFileLookup>(() => GetLookup(file));
                                    try {
                                        var data = new Task<Task<string>>(
                                            async () => new(
                                                (await lookup.Value.ReadAll(cancellationToken))
                                                .Select(x => (char) x)
                                                .ToArray()),
                                            cancellationToken);
                                        if (await matcher.Matches(this, file, lookup, data, stopwatch, timeoutPerEntry,
                                                cancellationToken))
                                            return () => {
                                                // Force name resolution
                                                _ = file.Name;
                                                fileFoundCallback(file);
                                            };
                                        else
                                            return null;
                                    } finally {
                                        if (lookup.IsValueCreated)
                                            lookup.Value.Dispose();
                                    }
                                default:
                                    return (Action?) null;
                            }
                        } finally {
                            stopwatches.Return(stopwatch);
                            progress.Progress++;
                        }
                    },
                    cancellationToken);
                _ = task.ContinueWith(x => {
                    if (x is {IsCompletedSuccessfully: true, Result: { } foundAction})
                        foundAction();
                }, cancellationToken);
                activeTasks.Add(task);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        await Task.WhenAll(activeTasks);

        progress.Completed = true;
        progressCallback(progress);
    }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
}
