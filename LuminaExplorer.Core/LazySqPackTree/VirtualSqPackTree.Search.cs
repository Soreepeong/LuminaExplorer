using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree.Matcher;
using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed partial class VirtualSqPackTree {
    public class SearchProgress {
        public readonly Stopwatch Stopwatch = new();

        public SearchProgress(object lastObject) {
            Total = 1;
            LastObject = lastObject;
        }

        public long Total { get; internal set; }
        public long Progress { get; internal set; }
        public object LastObject { get; internal set; }
        public bool Completed { get; internal set; }
    }

    public Task Search(
        VirtualFolder rootFolder,
        string query,
        Action<SearchProgress> progressCallback,
        Action<VirtualFolder> folderFoundCallback,
        Action<VirtualFile> fileFoundCallback,
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

        var progress = new SearchProgress(rootFolder);

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
            await queue.Writer.WriteAsync(
                (await asFileNamesResolved.ConfigureAwait(false)).Files,
                cancellationToken).ConfigureAwait(false);
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
                case List<VirtualFolder> folders:
                    itemList.AddRange(folders);
                    break;
                case List<VirtualFile> files:
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
                                    var lookup = new Lazy<VirtualFileLookup>(() => GetLookup(file));
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
