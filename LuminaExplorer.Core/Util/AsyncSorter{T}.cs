using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public class AsyncListSorter<T> : IComparer<int> {
    private const int AsyncSortThreshold = 4096;

    private readonly List<T> _list;
    private readonly T[] _array;
    private readonly T[] _mergeScratch;
    private int[]? _indexArray;
    private int[]? _indexArrayMergeScratch;
    private IComparer<T>? _comparer;
    private CancellationToken _cancellationToken;
    private Action<double>? _progressReport;
    private TimeSpan _progressReportInterval = TimeSpan.FromMilliseconds(200);
    private int _numThreads = Environment.ProcessorCount;
    private TaskScheduler? _taskScheduler;

    public AsyncListSorter(List<T> list) {
        _list = list;
        _array = (T[]) list.GetType()
            .GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(list)!;
        _mergeScratch = new T[_list.Count];
    }

    public AsyncListSorter<T> WithOrderMap() {
        _indexArray = new int[_list.Count];
        _indexArrayMergeScratch = new int[_list.Count];
        for (var i = 0; i < _list.Count; i++)
            _indexArray[i] = i;
        return this;
    }

    public AsyncListSorter<T> WithTaskScheduler(TaskScheduler taskScheduler) {
        _taskScheduler = taskScheduler;
        return this;
    }

    public AsyncListSorter<T> WithThreads(int numThreads) {
        _numThreads = numThreads;
        return this;
    }

    public AsyncListSorter<T> With(IComparer<T> comparison) {
        _comparer = comparison;
        return this;
    }

    public AsyncListSorter<T> With(Comparison<T> comparison) {
        _comparer = new ComparisonWrapper(comparison);
        return this;
    }

    public AsyncListSorter<T> WithCancellationToken(CancellationToken cancellationToken) {
        _cancellationToken = cancellationToken;
        return this;
    }

    public AsyncListSorter<T> WithProgrssCallback(Action<double> callback) {
        _progressReport = callback;
        return this;
    }

    public AsyncListSorter<T> WithProgrssCallbackInterval(TimeSpan interval) {
        _progressReportInterval = interval;
        return this;
    }

    // public Task<List<T>> Sort() => Sort(0, _list.Length);

    public Task<SortResult> Sort() => Sort(0, _list.Count);

    public async Task<SortResult> Sort(int index, int count) {
        if (index + count > _list.Count)
            throw new IndexOutOfRangeException(nameof(index));

        if (count <= AsyncSortThreshold) {
            if (_indexArray is null) {
                Array.Sort(_array, index, count, _comparer);
                return new(_list, null);
            }

            Array.Sort(_indexArray, index, count, this);
            return new(_indexArray.Select(i => _array[i]).ToList(), _indexArray);
        }

        var maxProgress = 1L;
        var currentProgress = 0L;

        var minimumUnit = count;
        while (minimumUnit * 2 > AsyncSortThreshold) {
            minimumUnit = (minimumUnit + 1) / 2;
            maxProgress++;
        }

        maxProgress *= count;

        var progressTimer = new Stopwatch();

        void MaybeReportProgress(bool force = false) {
            if (_progressReport is null || (!force && progressTimer.Elapsed < _progressReportInterval))
                return;

            progressTimer.Restart();
            _progressReport(1.0 * currentProgress / maxProgress);
        }

        MaybeReportProgress(true);

        var tasks = new List<Task<int>>(_numThreads);

        for (int pass = 0, unit = minimumUnit; unit < count; unit *= 2, pass++) {
            for (int i = index, remaining = count;; i += unit * 2, remaining -= unit * 2) {
                _cancellationToken.ThrowIfCancellationRequested();

                while (tasks.Count > _numThreads || (remaining <= 0 && tasks.Any())) {
                    _cancellationToken.ThrowIfCancellationRequested();
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(x => {
                        if (!x.IsCompleted)
                            return false;
                        currentProgress += x.Result;
                        MaybeReportProgress();
                        return true;
                    });
                }

                if (remaining <= 0)
                    break;

                if (pass == 0) {
                    var innerIndex = i;
                    var innerCount = Math.Min(unit * 2, remaining);
                    if (_taskScheduler is { } scheduler) {
                        tasks.Add(Task.Factory.StartNew(
                            () => {
                                if (_indexArray is null)
                                    Array.Sort(_array, innerIndex, innerCount, _comparer);
                                else
                                    Array.Sort(_indexArray, innerIndex, innerCount, this);

                                return innerCount;
                            },
                            _cancellationToken,
                            TaskCreationOptions.None,
                            scheduler));
                    } else {
                        tasks.Add(Task.Factory.StartNew(
                            () => {
                                if (_indexArray is null)
                                    Array.Sort(_array, innerIndex, innerCount, _comparer);
                                else
                                    Array.Sort(_indexArray, innerIndex, innerCount, this);

                                return innerCount;
                            },
                            _cancellationToken));
                    }
                } else {
                    var left = i;
                    var mid = left + Math.Min(unit, count - left);
                    var right = mid + Math.Min(unit, count - mid);
                    if (right == mid) {
                        currentProgress += right - mid;
                        MaybeReportProgress();
                    } else {
                        if (_indexArray is null) {
                            if (_taskScheduler is { } scheduler) {
                                tasks.Add(Task.Factory.StartNew(
                                    () => Merge(left, mid, right),
                                    _cancellationToken,
                                    TaskCreationOptions.None,
                                    scheduler));
                            } else {
                                tasks.Add(Task.Factory.StartNew(
                                    () => Merge(left, mid, right),
                                    _cancellationToken));
                            }
                        } else {
                            if (_taskScheduler is { } scheduler) {
                                tasks.Add(Task.Factory.StartNew(
                                    () => MergeIndices(left, mid, right),
                                    _cancellationToken,
                                    TaskCreationOptions.None,
                                    scheduler));
                            } else {
                                tasks.Add(Task.Factory.StartNew(
                                    () => MergeIndices(left, mid, right),
                                    _cancellationToken));
                            }
                        }
                    }
                }
            }
        }

        currentProgress = maxProgress;
        MaybeReportProgress(true);

        return _indexArray is null
            ? new(_list, null)
            : new(_indexArray.Select(i => _array[i]).ToList(), _indexArray);
    }

    private int Merge(int leftIndex, int midIndex, int rightIndex) {
        var r1 = leftIndex;
        var r2 = midIndex;
        var w = leftIndex;
        while (r1 < midIndex && r2 < rightIndex) {
            _cancellationToken.ThrowIfCancellationRequested();
            var cmp = Compare(r1, r2);
            switch (cmp) {
                case < 0:
                    _mergeScratch[w++] = _array[r1++];
                    break;
                case > 0:
                    _mergeScratch[w++] = _array[r2++];
                    break;
                default:
                    _mergeScratch[w++] = _array[r1++];
                    _mergeScratch[w++] = _array[r2++];
                    break;
            }
        }

        Array.Copy(_array, r1, _mergeScratch, w, midIndex - r1);
        w += midIndex - r1;
        Array.Copy(_array, r2, _mergeScratch, w, rightIndex - r2);
        Array.Copy(_mergeScratch, leftIndex, _array, leftIndex, rightIndex - leftIndex);

        return rightIndex - leftIndex;
    }

    private int MergeIndices(int leftIndex, int midIndex, int rightIndex) {
        Debug.Assert(_indexArray is not null);
        Debug.Assert(_indexArrayMergeScratch is not null);
        var r1 = leftIndex;
        var r2 = midIndex;
        var w = leftIndex;
        while (r1 < midIndex && r2 < rightIndex) {
            _cancellationToken.ThrowIfCancellationRequested();
            var cmp = Compare(_indexArray[r1], _indexArray[r2]);
            switch (cmp) {
                case < 0:
                    _indexArrayMergeScratch[w++] = _indexArray[r1++];
                    break;
                case > 0:
                    _indexArrayMergeScratch[w++] = _indexArray[r2++];
                    break;
                default:
                    _indexArrayMergeScratch[w++] = _indexArray[r1++];
                    _indexArrayMergeScratch[w++] = _indexArray[r2++];
                    break;
            }
        }

        Array.Copy(_indexArray, r1, _indexArrayMergeScratch, w, midIndex - r1);
        w += midIndex - r1;
        Array.Copy(_indexArray, r2, _indexArrayMergeScratch, w, rightIndex - r2);
        Array.Copy(_indexArrayMergeScratch, leftIndex, _indexArray, leftIndex, rightIndex - leftIndex);

        return rightIndex - leftIndex;
    }

    public int Compare(int x, int y) {
        if (_comparer is not null)
            return _comparer.Compare(_array[x], _array[y]);
        if (_array[x] is IComparable leftcmp)
            return leftcmp.CompareTo(_array[y]);
        throw new NotSupportedException();
    }

    private class ComparisonWrapper : IComparer<T> {
        private readonly Comparison<T> _comparison;

        public ComparisonWrapper(Comparison<T> comparison) {
            _comparison = comparison;
        }

        public int Compare(T? x, T? y) => _comparison(x!, y!);
    }

    public class SortResult {
        private readonly Lazy<int[]?> _reverseOrderMap;
        
        public List<T> Data;
        public int[]? OrderMap;

        public SortResult(List<T> data, int[]? orderMap) {
            Data = data;
            OrderMap = orderMap;
            _reverseOrderMap = orderMap is null ? new((int[]?)null) : new(() => {
                var indices = new int[orderMap.Length];
                for (var i = 0; i < orderMap.Length; i++)
                    indices[orderMap[i]] = i;
                return indices;
            });
        }

        public int[]? ReverseOrderMap => _reverseOrderMap.Value;
    }
}
