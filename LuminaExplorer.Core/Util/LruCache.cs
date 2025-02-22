using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public sealed class LruCache<TKey, TValue> : IDisposable, IEnumerable<LruCache<TKey, TValue>.LruCacheItem>
    where TKey : notnull
    where TValue : notnull {
    private readonly Dictionary<TKey, LinkedListNode<LruCacheItem>> _entryLookup = new();
    private LinkedList<LruCacheItem> _entries = new();

    private int _capacity;
    private readonly bool _disposeOnAnyThread;

    public LruCache(int capacity, bool disposeOnAnyThread)
    {
        this._capacity = capacity;
        this._disposeOnAnyThread = disposeOnAnyThread;
    }

    public int Capacity {
        get => this._capacity;
        set {
            this._capacity = value;
            while (this._entryLookup.Count >= this._capacity) this.RemoveFirst();
        }
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (this._entryLookup.TryGetValue(key, out var node)) {
            value = node.Value.Value;
            this._entries.Remove(node);
            this._entries.AddLast(node);
            return true;
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue val)
    {
        if (this._entryLookup.TryGetValue(key, out var existingNode)) {
            this._entries.Remove(existingNode);
            if (!EqualityComparer<TValue>.Default.Equals(existingNode.Value.Value, val)) {
                if (existingNode.Value.Value is IDisposable disposable)
                    disposable.Dispose();
            }
        } else if (this._entryLookup.Count >= this._capacity) this.RemoveFirst();

        var cacheItem = new LruCacheItem(key, val);
        var node = new LinkedListNode<LruCacheItem>(cacheItem);
        this._entries.AddLast(node);
        this._entryLookup[key] = node;
    }

    public void Flush()
    {
        this._entryLookup.Clear();
        if (this._disposeOnAnyThread) {
            var entries = this._entries;
            Task.Run(
                () => {
                    foreach (var e in entries) {
                        if (e.Value is IDisposable disposable)
                            disposable.Dispose();
                    }
                });
            this._entries = new();
        } else {
            foreach (var e in this._entries)
                if (e.Value is IDisposable disposable)
                    disposable.Dispose();
            this._entries.Clear();
        }
    }

    private void RemoveFirst()
    {
        var node = this._entries.First!;
        this._entries.RemoveFirst();
        this._entryLookup.Remove(node.Value.Key);
        if (node.Value.Value is IDisposable disposable)
            disposable.Dispose();
    }

    public class LruCacheItem {
        public readonly TKey Key;
        public readonly TValue Value;

        public LruCacheItem(TKey k, TValue v)
        {
            this.Key = k;
            this.Value = v;
        }
    }

    public void Dispose() => this.Flush();

    public IEnumerator<LruCacheItem> GetEnumerator() => this._entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
