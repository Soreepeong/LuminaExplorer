using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Physical;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class VirtualObjectImageLoader : IDisposable {
        private readonly object _syncRoot = new();

        private readonly Task[] _workers;
        private readonly CancellationTokenSource _disposing = new();
        private readonly SemaphoreSlim _requestSemaphore = new(0, 1);

        private readonly LruCache<IVirtualFile, PendingItem> _previews = new(128, true);
        private readonly Deque<Tuple<VirtualObject, IVirtualFile>> _requestsOrdered = new();
        private readonly HashSet<VirtualObject> _requests = new();

        private float _cropThresholdAspectRatioRatio = 2;
        private InterpolationMode _interpolationMode = InterpolationMode.Default;
        private int _width;
        private int _height;
        private int _configurationGeneration;

        public VirtualObjectImageLoader(int numThreads = default)
        {
            if (numThreads == default)
                numThreads = Environment.ProcessorCount;

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
            try {
                Task.WaitAll(this._workers);
            } catch (Exception) {
                // ignore
            }

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

                this._previews.Capacity = value;
                lock (this._syncRoot) {
                    while (this._requests.Count > value) {
                        var (vo, _) = this._requestsOrdered.RemoveFront();
                        this._requests.Remove(vo);
                    }
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

        public void Flush()
        {
            lock (this._syncRoot) {
                foreach (var f in this._previews)
                    f.Value.ShouldReload = true;
                this._requests.Clear();
                this._requestsOrdered.Clear();
                this._configurationGeneration++;
            }
        }

        public bool TryGetBitmap(
            VirtualObject virtualObject,
            [MaybeNullWhen(false)] out Bitmap bitmap,
            out bool isAssociationIcon)
        {
            bitmap = null!;
            isAssociationIcon = false;

            if (virtualObject.IsFolder)
                return false;

            if (this._previews.Capacity == 0)
                return false;

            lock (this._syncRoot) {
                if (this._previews.TryGet(virtualObject.File, out var task)) {
                    bitmap = task.Bitmap;
                    isAssociationIcon = task.IsAssociationIcon;
                }

                if (task?.ShouldReload is not false && !this._requests.Contains(virtualObject)) {
                    this._requests.Add(virtualObject);

                    while (this._requests.Count > this._previews.Capacity) {
                        var (vo, _) = this._requestsOrdered.RemoveFront();
                        this._requests.Remove(vo);
                    }

                    this._requestsOrdered.AddBack(Tuple.Create(virtualObject, virtualObject.File));

                    if (this._requestSemaphore.CurrentCount == 0) this._requestSemaphore.Release();
                }
            }

            return bitmap is not null;
        }

        private async Task WorkerBody()
        {
            while (!this._disposing.IsCancellationRequested) {
                int configurationGenerationOnTaking;
                VirtualObject virtualObject;
                IVirtualFile vfile;
                while (true) {
                    lock (this._syncRoot) {
                        if (!this._requestsOrdered.IsEmpty) {
                            (virtualObject, vfile) = this._requestsOrdered.RemoveBack();
                            configurationGenerationOnTaking = this._configurationGeneration;

                            if (!this._previews.TryGet(vfile, out var previousItem))
                                break;

                            if (previousItem.Bitmap is null || previousItem.ShouldReload)
                                break;

                            lock (this._syncRoot) this._requests.Remove(virtualObject);
                        }

                        if (!this._requestsOrdered.IsEmpty)
                            continue;
                    }

                    await this._requestSemaphore.WaitAsync(this._disposing.Token);
                }

                var item = new PendingItem();
                Bitmap? sourceBitmap = null;
                Bitmap? targetBitmap = null;
                IVirtualFileLookup? lookup = null;
                try {
                    if (!virtualObject.TryGetLookup(out lookup) || !Equals(lookup.File, vfile))
                        continue;

                    var definitelyTexture = false;
                    definitelyTexture |= lookup.Type == FileType.Texture;
                    definitelyTexture |= Path.GetExtension(vfile.Name).ToLowerInvariant() is ".tex" or ".atex";

                    var mightBeTexture = false;
                    mightBeTexture |= definitelyTexture;
                    mightBeTexture |= ImagingExtensions.ThumbnailSupportedExtensions.Any(
                        x => vfile.Name.EndsWith(x, StringComparison.InvariantCultureIgnoreCase));
                    // may be an .atex file
                    mightBeTexture |= !vfile.NameResolved && lookup is { Type: FileType.Standard, Size: > 256 };

                    if (!mightBeTexture) {
                        if (vfile is PhysicalFile pf) {
                            var icon = Icon.ExtractAssociatedIcon(pf.FileInfo.FullName);
                            if (icon != null) {
                                using (icon)
                                    item.Bitmap = icon.ToBitmap();
                                item.IsAssociationIcon = true;
                            }
                        }

                        item.CompletionSource.SetResult();
                        continue;
                    }

                    var w = this.Width;
                    var h = this.Height;
                    await using var stream = lookup.CreateStream();

                    if (definitelyTexture)
                        sourceBitmap = await stream.ExtractMipmapOfSizeAtLeastForTex(
                            Math.Max(w, h),
                            virtualObject.PlatformId,
                            this._disposing.Token);
                    else
                        sourceBitmap = await stream.ExtractMipmapOfSizeAtLeast(
                            Math.Max(w, h),
                            virtualObject.PlatformId,
                            this._disposing.Token);

                    if (this._disposing.IsCancellationRequested)
                        return;

                    if (sourceBitmap.Width <= w && sourceBitmap.Height <= w) {
                        item.Bitmap = sourceBitmap;
                        sourceBitmap = null;
                        item.CompletionSource.SetResult();
                        continue;
                    }

                    var srcRect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);

                    var sourceAspectRatio = (float) sourceBitmap.Height / sourceBitmap.Width;
                    var targetAspectRatio = (float) w / h;
                    if (sourceAspectRatio < targetAspectRatio) {
                        // horizontally wider
                        if (sourceAspectRatio < targetAspectRatio / this._cropThresholdAspectRatioRatio) {
                            sourceAspectRatio = targetAspectRatio / this._cropThresholdAspectRatioRatio;
                            srcRect.Width = (int) (sourceBitmap.Height / sourceAspectRatio);
                            srcRect.X = (sourceBitmap.Width - srcRect.Width) / 2;
                        }

                        // fit height
                        h = (int) (w * sourceAspectRatio);
                    } else {
                        // vertically wider
                        if (sourceAspectRatio > targetAspectRatio * this._cropThresholdAspectRatioRatio) {
                            sourceAspectRatio = targetAspectRatio * this._cropThresholdAspectRatioRatio;
                            srcRect.Height = (int) (sourceBitmap.Width * sourceAspectRatio);
                            srcRect.Y = (sourceBitmap.Height - srcRect.Height) / 2;
                        }

                        // fit width
                        w = (int) (h / sourceAspectRatio);
                    }

                    targetBitmap = new(w, h, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(targetBitmap);
                    g.InterpolationMode = this._interpolationMode;
                    g.DrawImage(sourceBitmap, new Rectangle(0, 0, w, h), srcRect, GraphicsUnit.Pixel);

                    item.Bitmap = targetBitmap;
                    targetBitmap = null;
                    item.CompletionSource.SetResult();
                } catch (Exception e) {
                    item.CompletionSource.SetException(e);
                } finally {
                    sourceBitmap?.Dispose();
                    targetBitmap?.Dispose();
                    lookup?.Dispose();
                    lock (this._syncRoot) {
                        if (this._requests.Remove(virtualObject)) {
                            // did any of the configuration get changed while the process?
                            if (configurationGenerationOnTaking == this._configurationGeneration) {
                                if (item.TaskStatus == TaskStatus.Created)
                                    item.CompletionSource.SetResult();
                                else
                                    this._previews.Add(vfile, item);
                                if (item.Bitmap is { } b) this.ImageLoaded?.Invoke(virtualObject, vfile, b);
                            } else {
                                item.Bitmap?.Dispose();

                                // if the object still points to a same file, queue the task again.
                                if (Equals(virtualObject.File, vfile)) {
                                    this._requests.Add(virtualObject);
                                    this._requestsOrdered.Add(Tuple.Create(virtualObject, vfile));
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
            public TaskCompletionSource CompletionSource = new();

            public TaskStatus TaskStatus => this.CompletionSource.Task.Status;

            public bool ShouldReload;

            public Bitmap? Bitmap;

            public bool IsAssociationIcon;

            public void Dispose()
            {
                if (this.CompletionSource.Task.Status == TaskStatus.Created)
                    return;

                this.CompletionSource.Task.ContinueWith(_ => { this.Bitmap?.Dispose(); });
            }
        }
    }
}
