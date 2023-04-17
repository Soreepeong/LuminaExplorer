using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree.Matcher;
using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed partial class VirtualSqPackTree {
    public Task Search(
        VirtualFolder rootFolder,
        string query,
        Action<VirtualFolder> folderFoundCallback,
        Action<VirtualFile> fileFoundCallback,
        TimeSpan timeoutPerEntry = default,
        CancellationToken cancellationToken = default) => Task.Factory.StartNew(async () => {
        if (new QueryTokenizer(query).Parse() is not { } matcher)
            return;

        var stopwatches = ObjectPool.Create(new DefaultPooledObjectPolicy<Stopwatch>());

        Debug.Print(matcher.ToString());

        if (timeoutPerEntry == default)
            timeoutPerEntry = TimeSpan.FromMilliseconds(500);

        cancellationToken.ThrowIfCancellationRequested();

        var queue = new List<object> {rootFolder};
        var activeTasks = new HashSet<Task>();

        while (queue.Any() || activeTasks.Any()) {
            cancellationToken.ThrowIfCancellationRequested();

            while (activeTasks.Count >= Environment.ProcessorCount)
                activeTasks.Remove(await Task.WhenAny(activeTasks));

            object folderOrFile;
            lock (queue) {
                folderOrFile = queue[^1];
                queue.RemoveAt(queue.Count - 1);
            }

            if (folderOrFile is VirtualFolder folder) {
                cancellationToken.ThrowIfCancellationRequested();

                var task = Task.Run(async () => {
                        await AsFilesResolved(folder);
                        var folders = GetFolders(folder);
                        lock (queue)
                            queue.AddRange(folders);
                        queue.AddRange(folder.Files);

                        var stopwatch = stopwatches.Get();
                        try {
                            return await matcher.Matches(this, folder, stopwatch, timeoutPerEntry, cancellationToken);
                        } finally {
                            stopwatches.Return(stopwatch);
                        }
                    },
                    cancellationToken);
                _ = task.ContinueWith(x => {
                    if (x is {IsCompletedSuccessfully: true, Result: true})
                        folderFoundCallback(folder);
                }, cancellationToken);
                activeTasks.Add(task);

            } else if (folderOrFile is VirtualFile file) {
                var task = Task.Run(async () => {
                        var lookup = new Lazy<VirtualFileLookup>(() => GetLookup(file));
                        var data = new Task<Task<string>>(
                            async () => new(
                                (await lookup.Value.ReadAll(cancellationToken))
                                .Select(x => (char) x)
                                .ToArray()),
                            cancellationToken);

                        var stopwatch = stopwatches.Get();
                        try {
                            return await matcher.Matches(this, file, lookup, data, stopwatch, timeoutPerEntry, cancellationToken);
                        } finally {
                            stopwatches.Return(stopwatch);
                        }
                    },
                    cancellationToken);
                _ = task.ContinueWith(x => {
                    if (x is {IsCompletedSuccessfully: true, Result: true})
                        fileFoundCallback(file);
                }, cancellationToken);
                activeTasks.Add(task);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        while (activeTasks.Any())
            activeTasks.Remove(await Task.WhenAny(activeTasks));
    }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
