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

    public AsyncListSorter(List<T> list)
    {
        this._list = list;
        this._array = (T[]) list.GetType()
            .GetField("_items", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(list)!;
        this._mergeScratch = new T[this._list.Count];
    }

    public AsyncListSorter<T> WithOrderMap()
    {
        this._indexArray = new int[this._list.Count];
        this._indexArrayMergeScratch = new int[this._list.Count];
        for (var i = 0; i < this._list.Count; i++) this._indexArray[i] = i;
        return this;
    }

    public AsyncListSorter<T> WithTaskScheduler(TaskScheduler taskScheduler)
    {
        this._taskScheduler = taskScheduler;
        return this;
    }

    public AsyncListSorter<T> WithThreads(int numThreads)
    {
        this._numThreads = numThreads;
        return this;
    }

    public AsyncListSorter<T> With(IComparer<T> comparison)
    {
        this._comparer = comparison;
        return this;
    }

    public AsyncListSorter<T> With(Comparison<T> comparison)
    {
        this._comparer = new ComparisonWrapper(comparison);
        return this;
    }

    public AsyncListSorter<T> WithCancellationToken(CancellationToken cancellationToken)
    {
        this._cancellationToken = cancellationToken;
        return this;
    }

    public AsyncListSorter<T> WithProgrssCallback(Action<double> callback)
    {
        this._progressReport = callback;
        return this;
    }

    public AsyncListSorter<T> WithProgrssCallbackInterval(TimeSpan interval)
    {
        this._progressReportInterval = interval;
        return this;
    }

    // public Task<List<T>> Sort() => Sort(0, _list.Length);

    public Task<SortResult> Sort() => this.Sort(0, this._list.Count);

    public async Task<SortResult> Sort(int index, int count)
    {
        if (index + count > this._list.Count)
            throw new IndexOutOfRangeException(nameof(index));

        if (count <= AsyncSortThreshold) {
            if (this._indexArray is null) {
                Array.Sort(this._array, index, count, this._comparer);
                return new(this._list, null);
            }

            Array.Sort(this._indexArray, index, count, this);
            return new(this._indexArray.Select(i => this._array[i]).ToList(), this._indexArray);
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

        void MaybeReportProgress(bool force = false)
        {
            if (this._progressReport is null || (!force && progressTimer.Elapsed < this._progressReportInterval))
                return;

            progressTimer.Restart();
            this._progressReport(1.0 * currentProgress / maxProgress);
        }

        MaybeReportProgress(true);

        var tasks = new List<Task<int>>(this._numThreads);

        for (int pass = 0, unit = minimumUnit; unit < count; unit *= 2, pass++) {
            for (int i = index, remaining = count;; i += unit * 2, remaining -= unit * 2) {
                this._cancellationToken.ThrowIfCancellationRequested();

                while (tasks.Count > this._numThreads || (remaining <= 0 && tasks.Any())) {
                    this._cancellationToken.ThrowIfCancellationRequested();
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(
                        x => {
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
                    if (this._taskScheduler is { } scheduler) {
                        tasks.Add(
                            Task.Factory.StartNew(
                                () => {
                                    if (this._indexArray is null)
                                        Array.Sort(this._array, innerIndex, innerCount, this._comparer);
                                    else
                                        Array.Sort(this._indexArray, innerIndex, innerCount, this);

                                    return innerCount;
                                },
                                this._cancellationToken,
                                TaskCreationOptions.None,
                                scheduler));
                    } else {
                        tasks.Add(
                            Task.Factory.StartNew(
                                () => {
                                    if (this._indexArray is null)
                                        Array.Sort(this._array, innerIndex, innerCount, this._comparer);
                                    else
                                        Array.Sort(this._indexArray, innerIndex, innerCount, this);

                                    return innerCount;
                                },
                                this._cancellationToken));
                    }
                } else {
                    var left = i;
                    var mid = left + Math.Min(unit, index + count - left);
                    var right = mid + Math.Min(unit, index + count - mid);
                    if (right == mid) {
                        currentProgress += right - mid;
                        MaybeReportProgress();
                    } else {
                        if (this._indexArray is null) {
                            if (this._taskScheduler is { } scheduler) {
                                tasks.Add(
                                    Task.Factory.StartNew(
                                        () => this.Merge(left, mid, right),
                                        this._cancellationToken,
                                        TaskCreationOptions.None,
                                        scheduler));
                            } else {
                                tasks.Add(
                                    Task.Factory.StartNew(
                                        () => this.Merge(left, mid, right),
                                        this._cancellationToken));
                            }
                        } else {
                            if (this._taskScheduler is { } scheduler) {
                                tasks.Add(
                                    Task.Factory.StartNew(
                                        () => this.MergeIndices(left, mid, right),
                                        this._cancellationToken,
                                        TaskCreationOptions.None,
                                        scheduler));
                            } else {
                                tasks.Add(
                                    Task.Factory.StartNew(
                                        () => this.MergeIndices(left, mid, right),
                                        this._cancellationToken));
                            }
                        }
                    }
                }
            }
        }

        currentProgress = maxProgress;
        MaybeReportProgress(true);

        return this._indexArray is null
            ? new(this._list, null)
            : new(this._indexArray.Select(i => this._array[i]).ToList(), this._indexArray);
    }

    private int Merge(int leftIndex, int midIndex, int rightIndex)
    {
        var r1 = leftIndex;
        var r2 = midIndex;
        var w = leftIndex;
        while (r1 < midIndex && r2 < rightIndex) {
            this._cancellationToken.ThrowIfCancellationRequested();
            var cmp = this.Compare(r1, r2);
            switch (cmp) {
                case < 0:
                    this._mergeScratch[w++] = this._array[r1++];
                    break;
                case > 0:
                    this._mergeScratch[w++] = this._array[r2++];
                    break;
                default:
                    this._mergeScratch[w++] = this._array[r1++];
                    this._mergeScratch[w++] = this._array[r2++];
                    break;
            }
        }

        Array.Copy(this._array, r1, this._mergeScratch, w, midIndex - r1);
        w += midIndex - r1;
        Array.Copy(this._array, r2, this._mergeScratch, w, rightIndex - r2);
        Array.Copy(this._mergeScratch, leftIndex, this._array, leftIndex, rightIndex - leftIndex);

        return rightIndex - leftIndex;
    }

    private int MergeIndices(int leftIndex, int midIndex, int rightIndex)
    {
        Debug.Assert(this._indexArray is not null);
        Debug.Assert(this._indexArrayMergeScratch is not null);
        var r1 = leftIndex;
        var r2 = midIndex;
        var w = leftIndex;
        while (r1 < midIndex && r2 < rightIndex) {
            this._cancellationToken.ThrowIfCancellationRequested();
            var cmp = this.Compare(this._indexArray[r1], this._indexArray[r2]);
            switch (cmp) {
                case < 0:
                    this._indexArrayMergeScratch[w++] = this._indexArray[r1++];
                    break;
                case > 0:
                    this._indexArrayMergeScratch[w++] = this._indexArray[r2++];
                    break;
                default:
                    this._indexArrayMergeScratch[w++] = this._indexArray[r1++];
                    this._indexArrayMergeScratch[w++] = this._indexArray[r2++];
                    break;
            }
        }

        Array.Copy(this._indexArray, r1, this._indexArrayMergeScratch, w, midIndex - r1);
        w += midIndex - r1;
        Array.Copy(this._indexArray, r2, this._indexArrayMergeScratch, w, rightIndex - r2);
        Array.Copy(this._indexArrayMergeScratch, leftIndex, this._indexArray, leftIndex, rightIndex - leftIndex);

        return rightIndex - leftIndex;
    }

    public int Compare(int x, int y)
    {
        if (this._comparer is not null)
            return this._comparer.Compare(this._array[x], this._array[y]);
        if (this._array[x] is IComparable leftcmp)
            return leftcmp.CompareTo(this._array[y]);
        throw new NotSupportedException();
    }

    private class ComparisonWrapper : IComparer<T> {
        private readonly Comparison<T> _comparison;

        public ComparisonWrapper(Comparison<T> comparison)
        {
            this._comparison = comparison;
        }

        public int Compare(T? x, T? y) => this._comparison(x!, y!);
    }

    public class SortResult {
        private readonly Lazy<int[]?> _reverseOrderMap;

        public List<T> Data;
        public int[]? OrderMap;

        public SortResult(List<T> data, int[]? orderMap)
        {
            this.Data = data;
            this.OrderMap = orderMap;
            this._reverseOrderMap = orderMap is null
                ? new((int[]?) null)
                : new(
                    () => {
                        var indices = new int[orderMap.Length];
                        for (var i = 0; i < orderMap.Length; i++)
                            indices[orderMap[i]] = i;
                        return indices;
                    });
        }

        public int[]? ReverseOrderMap => this._reverseOrderMap.Value;
    }
}
