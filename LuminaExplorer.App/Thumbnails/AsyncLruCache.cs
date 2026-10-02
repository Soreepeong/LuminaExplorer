using System;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>A small thread-safe LRU cache of values that are loaded asynchronously.</summary>
/// <remarks>A load is shared between all callers and is not cancelled when a caller cancels waiting for it.
/// Values are not disposed on eviction, as other callers may still be using them.</remarks>
public sealed class AsyncLruCache<TKey, TValue> : IDisposable where TKey : notnull {
    // Wrapped, so that LruCache does not try to dispose the (possibly still running) task on eviction.
    private readonly LruCache<TKey, Entry> _cache;

    public AsyncLruCache(int capacity)
    {
        this._cache = new(capacity, false);
    }

    public void Dispose()
    {
        lock (this._cache)
            this._cache.Flush();
    }

    public Task<TValue> GetOrAdd(TKey key, Func<Task<TValue>> factory, CancellationToken cancellationToken)
    {
        Task<TValue> task;
        lock (this._cache) {
            if (this._cache.TryGet(key, out var entry) && entry.Task is { IsFaulted: false, IsCanceled: false }) {
                task = entry.Task;
            } else {
                task = Task.Run(factory);
                this._cache.Add(key, new(task));
            }
        }

        return task.WaitAsync(cancellationToken);
    }

    private sealed record Entry(Task<TValue> Task);
}
