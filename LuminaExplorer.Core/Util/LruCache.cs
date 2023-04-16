namespace LuminaExplorer.Core.Util;

public class LruCache<TKey, TValue>
    where TKey : notnull
    where TValue : notnull {
    private readonly object _syncRoot = new();
    private readonly Dictionary<TKey, LinkedListNode<LruCacheItem>> _entryLookup = new();
    private readonly LinkedList<LruCacheItem> _entries = new();

    private readonly int _capacity;

    public LruCache(int capacity) {
        _capacity = capacity;
    }

    public bool TryGet(TKey key, out TValue value) {
        lock (_syncRoot) {
            if (_entryLookup.TryGetValue(key, out var node)) {
                value = node.Value.Value;
                _entries.Remove(node);
                _entries.AddLast(node);
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Add(TKey key, TValue val) {
        lock (_syncRoot) {
            if (_entryLookup.TryGetValue(key, out var existingNode))
                _entries.Remove(existingNode);
            else if (_entryLookup.Count >= _capacity)
                RemoveFirst();

            var cacheItem = new LruCacheItem(key, val);
            var node = new LinkedListNode<LruCacheItem>(cacheItem);
            _entries.AddLast(node);
            _entryLookup[key] = node;
        }
    }

    public void Flush() {
        lock (_syncRoot) {
            _entryLookup.Clear();
            _entries.Clear();
        }
    }

    private void RemoveFirst() {
        var node = _entries.First!;
        _entries.RemoveFirst();
        _entryLookup.Remove(node.Value.Key);
    }

    public class LruCacheItem {
        public readonly TKey Key;
        public readonly TValue Value;

        public LruCacheItem(TKey k, TValue v) {
            Key = k;
            Value = v;
        }
    }
}
