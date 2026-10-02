using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.App.Thumbnails;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class VirtualObjectImageLoader : IDisposable {
        private readonly object _syncRoot = new();

        private readonly Task[] _workers;
        private readonly CancellationTokenSource _disposing = new();
        private readonly SemaphoreSlim _requestSemaphore = new(0, 1);

        private readonly ThumbnailProviderRegistry _registry;
        private readonly ThumbnailContext _context;
        private readonly IVirtualFileSystem? _vfs;

        private readonly LruCache<IVirtualFile, CacheItem> _previews = new(128, true);

        // Pending requests; the newest one is at the back, and is processed first.
        private readonly Deque<Request> _requestsOrdered = new();

        // Pending and running requests.
        private readonly Dictionary<VirtualObject, Request> _requests = new();
        private readonly HashSet<Request> _running = new();

        private float _cropThresholdAspectRatioRatio = 2;
        private InterpolationMode _interpolationMode = InterpolationMode.Default;
        private int _width;
        private int _height;
        private float _dpiScale = 1;
        private int _configurationGeneration;
        private long _requestSequence;

        public VirtualObjectImageLoader(IVirtualFileSystem? vfs, int numThreads = default)
        {
            if (numThreads == default)
                numThreads = Environment.ProcessorCount;

            this._vfs = vfs;
            this._registry = ThumbnailProviderRegistry.Default;
            this._context = new(vfs);

            this._workers = Enumerable
                .Range(0, numThreads)
                .Select(
                    _ => Task.Factory.StartNew(
                        this.WorkerBody,
                        this._disposing.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default).Unwrap())
                .ToArray();
        }

        public void Dispose()
        {
            this._disposing.Cancel();
            this.CancelAll();
            try {
                Task.WaitAll(this._workers);
            } catch (Exception) {
                // ignore
            }

            this._context.Dispose();
            lock (this._syncRoot) this._previews.Dispose();
        }

        public event Action<VirtualObject, IVirtualFile, Bitmap>? ImageLoaded;

        public int Width {
            get => this._width;
            set {
                if (this._width == value)
                    return;

                this._width = value;
                this.Flush();
            }
        }

        public int Height {
            get => this._height;
            set {
                if (this._height == value)
                    return;

                this._height = value;
                this.Flush();
            }
        }

        public int Capacity {
            get => this._previews.Capacity;
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);

                lock (this._syncRoot) {
                    this._previews.Capacity = value;
                    this.TrimRequests();
                }
            }
        }

        public InterpolationMode InterpolationMode {
            get => this._interpolationMode;
            set {
                if (value == this._interpolationMode)
                    return;
                this._interpolationMode = value;
                this.Flush();
            }
        }

        public float DpiScale {
            get => this._dpiScale;
            set {
                if (EqualityComparer<float>.Default.Equals(this._dpiScale, value))
                    return;
                this._dpiScale = value;
                this.Flush();
            }
        }

        public int Threads => this._workers.Length;

        public float CropThresholdAspectRatioRatio {
            get => this._cropThresholdAspectRatioRatio;
            set {
                if (EqualityComparer<float>.Default.Equals(this._cropThresholdAspectRatioRatio, value))
                    return;

                this._cropThresholdAspectRatioRatio = value;
                this.Flush();
            }
        }

        /// <summary>Marks all thumbnails as outdated, and cancels all requests.</summary>
        public void Flush()
        {
            lock (this._syncRoot) {
                foreach (var f in this._previews)
                    f.Value.ShouldReload = true;
                this._configurationGeneration++;
                this.CancelAllNoLock();
            }
        }

        /// <summary>Cancels all pending and running requests, such as when the displayed folder changes.</summary>
        public void CancelAll()
        {
            lock (this._syncRoot)
                this.CancelAllNoLock();
        }

        public bool TryGetBitmap(
            VirtualObject virtualObject,
            [MaybeNullWhen(false)] out Bitmap bitmap,
            out ThumbnailKind kind)
        {
            bitmap = null!;
            kind = default;

            if (virtualObject.IsFolder)
                return false;

            if (this._previews.Capacity == 0)
                return false;

            lock (this._syncRoot) {
                if (this._previews.TryGet(virtualObject.File, out var item)) {
                    bitmap = item.Result?.Bitmap;
                    kind = item.Result?.Kind ?? default;
                }

                if (item?.ShouldReload is not false && !this._requests.ContainsKey(virtualObject)) {
                    var request = new Request(
                        virtualObject,
                        virtualObject.File,
                        ++this._requestSequence,
                        this._configurationGeneration,
                        CancellationTokenSource.CreateLinkedTokenSource(this._disposing.Token));
                    this._requests.Add(virtualObject, request);
                    this._requestsOrdered.AddBack(request);
                    this.TrimRequests();

                    if (this._requestSemaphore.CurrentCount == 0) this._requestSemaphore.Release();
                }
            }

            return bitmap is not null;
        }

        /// <summary>Cancels the oldest requests if there are more requests than the cache can hold.</summary>
        /// <remarks>Running requests that are older than the last <see cref="Capacity"/> requests are cancelled too,
        /// as they are most likely not visible anymore.</remarks>
        private void TrimRequests()
        {
            while (this._requestsOrdered.Count > this._previews.Capacity)
                this.CancelNoLock(this._requestsOrdered.RemoveFront());

            if (this._running.Count == 0)
                return;

            var oldestToKeep = this._requestSequence - this._previews.Capacity;
            foreach (var request in this._running.Where(x => x.Sequence <= oldestToKeep).ToArray())
                this.CancelNoLock(request);
        }

        private void CancelAllNoLock()
        {
            while (!this._requestsOrdered.IsEmpty)
                this.CancelNoLock(this._requestsOrdered.RemoveFront());
            foreach (var request in this._running.ToArray())
                this.CancelNoLock(request);
            this._requests.Clear();
        }

        private void CancelNoLock(Request request)
        {
            if (this._requests.TryGetValue(request.VirtualObject, out var r) && r == request)
                this._requests.Remove(request.VirtualObject);

            // Running requests get removed from _running when the worker notices.
            if (!this._running.Contains(request))
                request.Cancellation.Dispose();
            else
                request.Cancellation.Cancel();
        }

        private async Task WorkerBody()
        {
            while (!this._disposing.IsCancellationRequested) {
                Request request;
                while (true) {
                    lock (this._syncRoot) {
                        if (!this._requestsOrdered.IsEmpty) {
                            request = this._requestsOrdered.RemoveBack();
                            this._running.Add(request);
                            break;
                        }
                    }

                    await this._requestSemaphore.WaitAsync(this._disposing.Token);
                }

                ThumbnailResult? result = null;
                Exception? exception = null;
                var cancellationToken = request.Cancellation.Token;
                try {
                    result = await this.CreateThumbnail(request, cancellationToken);
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // pass
                } catch (Exception e) {
                    exception = e;
                    Debug.WriteLine($"Failed to create thumbnail of {request.File.Name}: {e}");
                }

                lock (this._syncRoot) {
                    this._running.Remove(request);
                    var isCurrent = this._requests.TryGetValue(request.VirtualObject, out var r) && r == request;
                    if (isCurrent)
                        this._requests.Remove(request.VirtualObject);

                    if (isCurrent &&
                        !cancellationToken.IsCancellationRequested &&
                        request.ConfigurationGeneration == this._configurationGeneration) {
                        // Failures are remembered too, so that they are not retried every time the item is drawn.
                        this._previews.Add(request.File, new(result));
                        if (result is not null)
                            this.ImageLoaded?.Invoke(request.VirtualObject, request.File, result.Bitmap);
                        else if (exception is not null)
                            Debug.WriteLine($"No thumbnail for {request.File.Name}");
                    } else {
                        // all our work has been for nothing
                        result?.Dispose();
                    }

                    request.Cancellation.Dispose();
                }
            }
        }

        private async Task<ThumbnailResult?> CreateThumbnail(Request request, CancellationToken cancellationToken)
        {
            var virtualObject = request.VirtualObject;
            if (!virtualObject.TryGetLookup(out var lookup) || !Equals(lookup.File, request.File))
                return null;

            uint? magic = null;
            if (ThumbnailRequest.ShouldReadMagic(request.File, lookup)) {
                var buf = new byte[4];
                await using var stream = lookup.CreateStream();
                if (await stream.ReadAtLeastAsync(buf, 4, false, cancellationToken) == 4)
                    magic = BinaryPrimitives.ReadUInt32LittleEndian(buf);
            }

            var thumbnailRequest = new ThumbnailRequest(
                this._vfs,
                request.File,
                lookup,
                magic,
                virtualObject.PlatformId,
                new(this._width, this._height, this._interpolationMode, this._cropThresholdAspectRatioRatio,
                    this._dpiScale));

            return await this._registry.CreateAsync(thumbnailRequest, this._context, cancellationToken);
        }

        private sealed class Request {
            public readonly VirtualObject VirtualObject;
            public readonly IVirtualFile File;
            public readonly long Sequence;
            public readonly int ConfigurationGeneration;
            public readonly CancellationTokenSource Cancellation;

            public Request(
                VirtualObject virtualObject,
                IVirtualFile file,
                long sequence,
                int configurationGeneration,
                CancellationTokenSource cancellation)
            {
                this.VirtualObject = virtualObject;
                this.File = file;
                this.Sequence = sequence;
                this.ConfigurationGeneration = configurationGeneration;
                this.Cancellation = cancellation;
            }
        }

        private sealed class CacheItem : IDisposable {
            public readonly ThumbnailResult? Result;

            public bool ShouldReload;

            public CacheItem(ThumbnailResult? result)
            {
                this.Result = result;
            }

            public void Dispose() => this.Result?.Dispose();
        }
    }
}
