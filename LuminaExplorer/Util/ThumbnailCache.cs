using System.Drawing.Imaging;
using Lumina.Data.Files;
using LuminaExplorer.LazySqPackTree;
using LuminaExplorer.LazySqPackTree.VirtualFileStream;

namespace LuminaExplorer.Util;

public class ThumbnailCache {
    public static readonly ThumbnailCache Instance = new();

    private const float CropThresholdAspectRatioRatio = 2;

    private readonly Dictionary<Tuple<object, int, int>, Task<Bitmap>> _cache = new();
    private readonly Queue<Tuple<object, int, int>> _taskQueue = new();

    private ThumbnailCache() { }

    public Task<Bitmap> LoadFrom(VirtualFile file, int w, int h, TextureVirtualFileStream tvfs) {
        var key = Tuple.Create((object) file, w, h);
        Task<Bitmap> task;
        lock (_cache) {
            if (_cache.TryGetValue(key, out task!))
                return task;

            _cache.Add(key, task = new(() => {
                var f = tvfs
                    .ExtractMipmapOfSizeAtLeast(Math.Max(w, h))
                    .Filter(format: TexFile.TextureFormat.B8G8R8A8);

                Bitmap sourceBitmap;
                unsafe {
                    fixed (void* p = f.RawData) {
                        sourceBitmap = new(f.Width, f.Height, 4 * f.Width,
                            PixelFormat.Format32bppArgb, (nint) p);
                    }
                }

                if (f.Width == w && f.Height == h)
                    return sourceBitmap;

                using (sourceBitmap) {
                    Bitmap? targetBitmap = null;
                    Graphics? g = null;
                    try {
                        g = Graphics.FromImage(targetBitmap = new(w, h, PixelFormat.Format32bppArgb));
                        var srcRect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
                        var destRect = new Rectangle(0, 0, w, h);

                        var sourceAspectRatio = (float) sourceBitmap.Height / sourceBitmap.Width;
                        var targetAspectRatio = (float) w / h;
                        if (sourceAspectRatio < targetAspectRatio) {
                            // horizontally wider
                            if (sourceAspectRatio < targetAspectRatio / CropThresholdAspectRatioRatio) {
                                sourceAspectRatio = targetAspectRatio / CropThresholdAspectRatioRatio;
                                srcRect.Width = (int) (sourceBitmap.Height / sourceAspectRatio);
                                srcRect.X = (sourceBitmap.Width - srcRect.Width) / 2;
                            }

                            // fit height
                            destRect.Height = (int) (destRect.Width * sourceAspectRatio);
                            destRect.Y = (h - destRect.Height) / 2;
                        } else {
                            // vertically wider
                            if (sourceAspectRatio > targetAspectRatio * CropThresholdAspectRatioRatio) {
                                sourceAspectRatio = targetAspectRatio * CropThresholdAspectRatioRatio;
                                srcRect.Height = (int) (sourceBitmap.Width * sourceAspectRatio);
                                srcRect.Y = (sourceBitmap.Height - srcRect.Height) / 2;
                            }

                            // fit width
                            destRect.Width = (int) (destRect.Height / sourceAspectRatio);
                            destRect.X = (w - destRect.Width) / 2;
                        }

                        g.DrawImage(sourceBitmap, destRect, srcRect, GraphicsUnit.Pixel);

                        var result = targetBitmap;
                        targetBitmap = null;
                        return result;
                    } finally {
                        g?.Dispose();
                        targetBitmap?.Dispose();
                    }
                }
            }));

            _taskQueue.Enqueue(key);
        }

        ProcessQueuedItems();

        return task;
    }

    private void ProcessQueuedItems() {
        if (!_taskQueue.Any())
            return;

        lock (_taskQueue) {
            if (_taskQueue.Count > Environment.ProcessorCount)
                return;

            if (!_taskQueue.TryDequeue(out var key))
                return;

            var task = _cache[key];
            task.ContinueWith(_ => ProcessQueuedItems());
            task.Start(TaskScheduler.Default);
        }
    }
}
