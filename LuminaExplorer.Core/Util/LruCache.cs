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

    public LruCache(int capacity, bool disposeOnAnyThread) {
        _capacity = capacity;
        _disposeOnAnyThread = disposeOnAnyThread;
    }

    public int Capacity {
        get => _capacity;
        set {
            _capacity = value;
            while (_entryLookup.Count >= _capacity)
                RemoveFirst();
        }
    }

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value) {
        if (_entryLookup.TryGetValue(key, out var node)) {
            value = node.Value.Value;
            _entries.Remove(node);
            _entries.AddLast(node);
            return true;
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue val) {
        if (_entryLookup.TryGetValue(key, out var existingNode)) {
            _entries.Remove(existingNode);
            if (!EqualityComparer<TValue>.Default.Equals(existingNode.Value.Value, val)) {
                if (existingNode.Value.Value is IDisposable disposable)
                    disposable.Dispose();
            }
        } else if (_entryLookup.Count >= _capacity)
            RemoveFirst();

        var cacheItem = new LruCacheItem(key, val);
        var node = new LinkedListNode<LruCacheItem>(cacheItem);
        _entries.AddLast(node);
        _entryLookup[key] = node;
    }

    public void Flush() {
        _entryLookup.Clear();
        if (_disposeOnAnyThread) {
            var entries = _entries;
            Task.Run(() => {
                foreach (var e in entries) {
                    if (e.Value is IDisposable disposable)
                        disposable.Dispose();
                }
            });
            _entries = new();
        } else {
            foreach (var e in _entries)
                if (e.Value is IDisposable disposable)
                    disposable.Dispose();
            _entries.Clear();
        }
    }

    private void RemoveFirst() {
        var node = _entries.First!;
        _entries.RemoveFirst();
        _entryLookup.Remove(node.Value.Key);
        if (node.Value.Value is IDisposable disposable)
            disposable.Dispose();
    }

    public class LruCacheItem {
        public readonly TKey Key;
        public readonly TValue Value;

        public LruCacheItem(TKey k, TValue v) {
            Key = k;
            Value = v;
        }
    }

    public void Dispose() => Flush();

    public IEnumerator<LruCacheItem> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
