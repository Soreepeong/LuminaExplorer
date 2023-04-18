using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.Util;

public class AsyncSorter<T> {
    private const int AsyncSortThreshold = 4096;

    private readonly ObjectPool<List<T>> _pool = ObjectPool.Create(new DefaultPooledObjectPolicy<List<T>>());
    private readonly ICollection<T> _source;
    private Comparison<T>? _comparison;
    private IComparer<T>? _comparer;
    private CancellationToken _cancellationToken;

    public AsyncSorter(ICollection<T> source) => _source = source;

    public AsyncSorter<T> With(IComparer<T> comparison) {
        _comparer = comparison;
        return this;
    }

    public AsyncSorter<T> With(Comparison<T> comparison) {
        _comparison = comparison;
        return this;
    }

    public AsyncSorter<T> With(CancellationToken cancellationToken) {
        _cancellationToken = cancellationToken;
        return this;
    }

    public Task<List<T>> Sort() => Sort(0, _source.Count);

    public async Task<List<T>> Sort(int from, int to) {
        if (to - from <= AsyncSortThreshold)
            return SortShort(from, to);

        var minimumUnit = to - from;
        while (minimumUnit > AsyncSortThreshold)
            minimumUnit = (minimumUnit + 1) / 2;

        var sortedLists = new List<List<T>>((to - from + minimumUnit - 1) / minimumUnit);

        var taskCount = Environment.ProcessorCount;
        var tasks = new List<Task<List<T>>>(taskCount);

        for (var i = from;; i += minimumUnit) {
            _cancellationToken.ThrowIfCancellationRequested();
            
            while (tasks.Count > taskCount || (i >= to && tasks.Any())) {
                _cancellationToken.ThrowIfCancellationRequested();
                await Task.WhenAny(tasks);
                tasks.RemoveAll(x => {
                    if (!x.IsCompleted)
                        return false;
                    sortedLists.Add(x.Result);
                    return true;
                });
            }

            if (i >= to)
                break;

            // "captured variable is modified".
            // ??
            {
                var subFrom = i;
                var subTo = Math.Min(i + minimumUnit, to);
                tasks.Add(Task.Run(() => SortShort(subFrom, subTo), _cancellationToken));
            }
        }

        var sortedLists2 = new List<List<T>>(sortedLists.Count);
        while (sortedLists.Count > 1) {
            _cancellationToken.ThrowIfCancellationRequested();
            
            (sortedLists, sortedLists2) = (sortedLists2, sortedLists);

            for (var i = 0;; i += 2) {
                _cancellationToken.ThrowIfCancellationRequested();
                
                while (tasks.Count > taskCount || (i >= sortedLists2.Count && tasks.Any())) {
                    _cancellationToken.ThrowIfCancellationRequested();
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(x => {
                        if (!x.IsCompleted)
                            return false;
                        sortedLists.Add(x.Result);
                        return true;
                    });
                }

                if (i >= sortedLists2.Count)
                    break;

                var list1 = sortedLists2[i];
                if (i + 1 == sortedLists2.Count)
                    sortedLists.Add(list1);
                else {
                    var list2 = sortedLists2[i + 1];
                    tasks.Add(Task.Run(() => Merge(list1, list2), _cancellationToken));
                }
            }

            sortedLists2.Clear();
        }

        return sortedLists.First();
    }

    private List<T> SortShort(int from, int to) {
        var res = _pool.Get();
        res.Clear();
        res.EnsureCapacity(to - from);
        res.AddRange(_source.Skip(from).Take(to - from));
        if (_comparison is not null)
            res.Sort(_comparison);
        else
            res.Sort(_comparer);
        return res;
    }

    private List<T> Merge(List<T> left, List<T> right) {
        var res = _pool.Get();
        res.Clear();
        res.EnsureCapacity(left.Count + right.Count);
        var l = 0;
        var r = 0;
        while (l < left.Count && r < right.Count) {
            _cancellationToken.ThrowIfCancellationRequested();
            int cmp;
            if (_comparison is not null)
                cmp = _comparison(left[l], right[r]);
            else if (_comparer is not null)
                cmp = _comparer.Compare(left[l], right[r]);
            else if (left[l] is IComparable leftcmp)
                cmp = leftcmp.CompareTo(right[r]);
            else
                throw new NotSupportedException();
            switch (cmp) {
                case < 0:
                    res.Add(left[l++]);
                    break;
                case > 0:
                    res.Add(right[r++]);
                    break;
                default:
                    res.Add(left[l++]);
                    res.Add(right[r++]);
                    break;
            }
        }

        res.AddRange(left.Skip(l));
        res.AddRange(right.Skip(r));
        _pool.Return(left);
        _pool.Return(right);
        return res;
    }
}
