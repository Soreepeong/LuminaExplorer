using System.Diagnostics;

namespace LuminaExplorer.Core.Util;

public class AsyncListSorter<T> {
    private const int AsyncSortThreshold = 4096;

    private readonly List<T> _list;
    private readonly T[] _array;
    private readonly T[] _mergeScratch;
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

    public Task<List<T>> Sort() => Sort(0, _list.Count);

    public async Task<List<T>> Sort(int index, int count) {
        if (index + count > _list.Count)
            throw new IndexOutOfRangeException(nameof(index));

        if (count <= AsyncSortThreshold) {
            Array.Sort(_array, index, count, _comparer);
            return _list;
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
                                Array.Sort(_array, innerIndex, innerCount, _comparer);
                                return innerCount;
                            },
                            _cancellationToken,
                            TaskCreationOptions.None,
                            scheduler));
                    } else {
                        tasks.Add(Task.Factory.StartNew(
                            () => {
                                Array.Sort(_array, innerIndex, innerCount, _comparer);
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
                    }
                }
            }
        }

        currentProgress = maxProgress;
        MaybeReportProgress(true);
        return _list;
    }

    private int Merge(int leftIndex, int midIndex, int rightIndex) {
        var r1 = leftIndex;
        var r2 = midIndex;
        var w = leftIndex;
        while (r1 < midIndex && r2 < rightIndex) {
            _cancellationToken.ThrowIfCancellationRequested();
            int cmp;
            if (_comparer is not null)
                cmp = _comparer.Compare(_array[r1], _array[r2]);
            else if (_array[r1] is IComparable leftcmp)
                cmp = leftcmp.CompareTo(_array[r2]);
            else
                throw new NotSupportedException();
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

    private class ComparisonWrapper : IComparer<T> {
        private readonly Comparison<T> _comparison;

        public ComparisonWrapper(Comparison<T> comparison) {
            _comparison = comparison;
        }

        public int Compare(T? x, T? y) => _comparison(x!, y!);
    }
}
