using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Files.Excel;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.VirtualFileSystem.Matcher;
using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.VirtualFileSystem;

public static class VirtualFileSystemExtensions {
    public static Task Search(
        this IVirtualFileSystem ivfs,
        IVirtualFolder rootFolder,
        string query,
        Action<IVirtualFileSystem.SearchProgress> progressCallback,
        Action<IVirtualFolder> folderFoundCallback,
        Action<IVirtualFile> fileFoundCallback,
        int numThreads = default,
        TimeSpan timeoutPerEntry = default,
        CancellationToken cancellationToken = default) => Task.Factory.StartNew(async () => {
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

        async Task Traverse(IVirtualFolder folder) {
            cancellationToken.ThrowIfCancellationRequested();

            await ivfs.AsFoldersResolved(folder);
            var folders = ivfs.GetFolders(folder);
            progress.Total += folders.Count;

            cancellationToken.ThrowIfCancellationRequested();

            var asFileNamesResolved = ivfs.AsFileNamesResolved(folder);
            await queue.Writer.WriteAsync(folders, cancellationToken).ConfigureAwait(false);
            foreach (var f in folders)
                await Traverse(f);

            var files = ivfs.GetFiles(await asFileNamesResolved);
            progress.Total += files.Count;
            await queue.Writer.WriteAsync(files, cancellationToken).ConfigureAwait(false);
        }

        long nextProgressReportedMilliseconds = 0;
        progress.Stopwatch.Start();

        _ = Task.Run(async () => {
            await queue.Writer.WriteAsync(new List<IVirtualFolder> {rootFolder}, cancellationToken);
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

                var task = Task.Run(
                    async () => {
                        var stopwatch = stopwatches.Get();
                        try {
                            switch (item) {
                                case IVirtualFolder folder:
                                    if (await matcher.Matches(ivfs, folder, stopwatch, timeoutPerEntry,
                                            cancellationToken))
                                        return () => folderFoundCallback(folder);
                                    else
                                        return null;
                                case IVirtualFile file:
                                    var lookup = new Lazy<IVirtualFileLookup>(() => ivfs.GetLookup(file));
                                    try {
                                        var data = new Task<Task<string>>(
                                            async () => new(
                                                // ReSharper disable once AccessToDisposedClosure
                                                (await lookup.Value.ReadAll(cancellationToken))
                                                .Select(x => (char) x)
                                                .ToArray()),
                                            cancellationToken);
                                        if (await matcher.Matches(ivfs, file, lookup, data, stopwatch, timeoutPerEntry,
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

    internal static bool GetFileResourceTypeByMagic(uint magic, [MaybeNullWhen(false)] out Type type) {
        type = magic switch {
            0x42444553u => typeof(ScdFile),
            0x46445845u => typeof(ExcelDataFile),
            0x46485845u => typeof(ExcelHeaderFile),
            0x544c5845u => typeof(ExcelListFile),
            ShcdHeader.MagicValue => typeof(ShcdFile),
            ShpkHeader.MagicValue => typeof(ShpkFile),
            PapFile.PapHeader.MagicValue => typeof(PapFile),
            EidFile.EidHeader.MagicValue => typeof(EidFile),
            SklbFile.SklbHeader.MagicValue => typeof(SklbFile),
            _ => null,
        };
        
        return type is not null;
    }
}
