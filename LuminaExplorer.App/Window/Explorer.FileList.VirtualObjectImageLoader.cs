using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class VirtualObjectImageLoader : IDisposable {
        private readonly object _syncRoot = new();
        
        private readonly Task[] _workers;
        private readonly CancellationTokenSource _disposing = new();
        private readonly SemaphoreSlim _requestSemaphore = new(0, 1);

        private readonly LruCache<VirtualFile, PendingItem> _previews = new(128, true);
        private readonly Deque<Tuple<VirtualObject, VirtualFile>> _requestsOrdered = new();
        private readonly HashSet<VirtualObject> _requests = new();

        private float _cropThresholdAspectRatioRatio = 2;
        private InterpolationMode _interpolationMode = InterpolationMode.Default;
        private int _width;
        private int _height;
        private int _configurationGeneration;

        public VirtualObjectImageLoader(int numThreads = default) {
            if (numThreads == default)
                numThreads = Environment.ProcessorCount;

            _workers = Enumerable
                .Range(0, numThreads)
                .Select(_ => Task.Factory.StartNew(
                    WorkerBody,
                    _disposing.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap())
                .ToArray();
        }

        public void Dispose() {
            _disposing.Cancel();
            try {
                Task.WaitAll(_workers);
            } catch (Exception) {
                // ignore
            }

            lock (_syncRoot)
                _previews.Dispose();
        }

        public event Action<VirtualObject, VirtualFile, Bitmap>? ImageLoaded;

        public int Width {
            get => _width;
            set {
                if (_width == value)
                    return;

                _width = value;
                Flush();
            }
        }

        public int Height {
            get => _height;
            set {
                if (_height == value)
                    return;

                _height = value;
                Flush();
            }
        }

        public int Capacity {
            get => _previews.Capacity;
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);

                _previews.Capacity = value;
                lock (_syncRoot) {
                    while (_requests.Count > value) {
                        var (vo, _) = _requestsOrdered.RemoveFront();
                        _requests.Remove(vo);
                    }
                }
            }
        }

        public InterpolationMode InterpolationMode {
            get => _interpolationMode;
            set {
                if (value == _interpolationMode)
                    return;
                _interpolationMode = value;
                Flush();
            }
        }

        public int Threads => _workers.Length;

        public float CropThresholdAspectRatioRatio {
            get => _cropThresholdAspectRatioRatio;
            set {
                if (EqualityComparer<float>.Default.Equals(_cropThresholdAspectRatioRatio, value))
                    return;

                _cropThresholdAspectRatioRatio = value;
                Flush();
            }
        }

        public void Flush() {
            lock (_syncRoot) {
                foreach (var f in _previews)
                    f.Value.ShouldReload = true;
                _requests.Clear();
                _requestsOrdered.Clear();
                _configurationGeneration++;
            }
        }

        public bool TryGetBitmap(VirtualObject virtualObject, [MaybeNullWhen(false)] out Bitmap bitmap) {
            bitmap = null!;

            if (virtualObject.IsFolder)
                return false;

            if (_previews.Capacity == 0)
                return false;

            lock (_syncRoot) {
                if (_previews.TryGet(virtualObject.File, out var task))
                    bitmap = task.Bitmap;

                if (task?.ShouldReload is not false && !_requests.Contains(virtualObject)) {
                    _requests.Add(virtualObject);

                    while (_requests.Count > _previews.Capacity) {
                        var (vo, _) = _requestsOrdered.RemoveFront();
                        _requests.Remove(vo);
                    }

                    _requestsOrdered.AddBack(Tuple.Create(virtualObject, virtualObject.File));

                    if (_requestSemaphore.CurrentCount == 0)
                        _requestSemaphore.Release();
                }
            }

            return bitmap is not null;
        }

        private async Task WorkerBody() {
            while (!_disposing.IsCancellationRequested) {
                int configurationGenerationOnTaking;
                VirtualObject virtualObject;
                VirtualFile virtualFile;
                while (true) {
                    lock (_syncRoot) {
                        if (!_requestsOrdered.IsEmpty) {
                            (virtualObject, virtualFile) = _requestsOrdered.RemoveBack();
                            configurationGenerationOnTaking = _configurationGeneration;

                            if (!_previews.TryGet(virtualFile, out var previousItem))
                                break;

                            if (previousItem.Bitmap is null || previousItem.ShouldReload)
                                break;
                            
                            lock (_syncRoot)
                                _requests.Remove(virtualObject);
                        }

                        if (!_requestsOrdered.IsEmpty)
                            continue;
                    }
                    
                    await _requestSemaphore.WaitAsync(_disposing.Token);
                }

                var item = new PendingItem();
                Bitmap? targetBitmap = null;
                try {
                    if (!virtualObject.TryGetLookup(out var lookup) || lookup.File != virtualFile)
                        continue;

                    var canBeTexture = false;
                    canBeTexture |= lookup.Type == FileType.Texture;
                    canBeTexture |= virtualFile.Name.EndsWith(".atex", StringComparison.InvariantCultureIgnoreCase);
                    // may be an .atex file
                    canBeTexture |= !virtualFile.NameResolved && lookup is {Type: FileType.Standard, Size: > 256};

                    if (!canBeTexture)
                        continue;

                    var w = Width;
                    var h = Height;
                    await using var stream = lookup.CreateStream();
                    var f = await stream.ExtractMipmapOfSizeAtLeast(
                        Math.Max(w, h),
                        virtualObject.PlatformId,
                        _disposing.Token);

                    if (_disposing.IsCancellationRequested)
                        return;

                    f = f.Filter(format: TexFile.TextureFormat.B8G8R8A8);

                    if (_disposing.IsCancellationRequested)
                        return;

                    unsafe {
                        fixed (void* p = f.RawData) {
                            using var sourceBitmap = new Bitmap(f.Width, f.Height, 4 * f.Width,
                                PixelFormat.Format32bppArgb, (nint) p);

                            // Note: sourceBitmap does not copy the given data. Making a copy is required.
                            if (f.Width <= w && f.Height <= w) {
                                item.CompletionSource.SetResult(new(sourceBitmap));
                                continue;
                            }

                            var srcRect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);

                            var sourceAspectRatio = (float) sourceBitmap.Height / sourceBitmap.Width;
                            var targetAspectRatio = (float) w / h;
                            if (sourceAspectRatio < targetAspectRatio) {
                                // horizontally wider
                                if (sourceAspectRatio < targetAspectRatio / _cropThresholdAspectRatioRatio) {
                                    sourceAspectRatio = targetAspectRatio / _cropThresholdAspectRatioRatio;
                                    srcRect.Width = (int) (sourceBitmap.Height / sourceAspectRatio);
                                    srcRect.X = (sourceBitmap.Width - srcRect.Width) / 2;
                                }

                                // fit height
                                h = (int) (w * sourceAspectRatio);
                            } else {
                                // vertically wider
                                if (sourceAspectRatio > targetAspectRatio * _cropThresholdAspectRatioRatio) {
                                    sourceAspectRatio = targetAspectRatio * _cropThresholdAspectRatioRatio;
                                    srcRect.Height = (int) (sourceBitmap.Width * sourceAspectRatio);
                                    srcRect.Y = (sourceBitmap.Height - srcRect.Height) / 2;
                                }

                                // fit width
                                w = (int) (h / sourceAspectRatio);
                            }

                            targetBitmap = new(w, h, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(targetBitmap)) {
                                g.InterpolationMode = _interpolationMode;
                                g.DrawImage(sourceBitmap, new Rectangle(0, 0, w, h), srcRect, GraphicsUnit.Pixel);
                            }
                        }
                    }

                    item.CompletionSource.SetResult(targetBitmap);
                    targetBitmap = null;

                } catch (Exception e) {
                    item.CompletionSource.SetException(e);

                } finally {
                    targetBitmap?.Dispose();
                    lock (_syncRoot) {
                        if (_requests.Remove(virtualObject)) {
                            // did any of the configuration get changed while the process?
                            if (configurationGenerationOnTaking == _configurationGeneration) {
                                if (item.TaskStatus == TaskStatus.Created)
                                    item.CompletionSource.SetResult(null);
                                else
                                    _previews.Add(virtualFile, item);
                                if (item.Bitmap is { } b)
                                    ImageLoaded?.Invoke(virtualObject, virtualFile, b);

                            } else {
                                item.Bitmap?.Dispose();

                                // if the object still points to a same file, queue the task again.
                                if (virtualObject.File == virtualFile) {
                                    _requests.Add(virtualObject);
                                    _requestsOrdered.Add(Tuple.Create(virtualObject, virtualFile));
                                }
                            }
                        } else {
                            // all our work has been for nothing
                            item.Bitmap?.Dispose();
                        }
                    }
                }
            }
        }

        private sealed class PendingItem : IDisposable {
            public readonly TaskCompletionSource<Bitmap?> CompletionSource = new();

            public TaskStatus TaskStatus => CompletionSource.Task.Status;

            public bool ShouldReload;

            public Bitmap? Bitmap =>
                CompletionSource.Task.IsCompletedSuccessfully ? CompletionSource.Task.Result : null;

            public void Dispose() {
                if (CompletionSource.Task.Status == TaskStatus.Created)
                    return;

                CompletionSource.Task.ContinueWith(result => {
                    if (result.IsCompletedSuccessfully)
                        result.Result?.Dispose();
                });
            }
        }
    }
}
