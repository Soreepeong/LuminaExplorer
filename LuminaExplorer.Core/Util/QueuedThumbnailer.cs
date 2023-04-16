﻿using System.Drawing.Imaging;
using Lumina.Data.Files;
using Lumina.Data.Structs;

namespace LuminaExplorer.Core.Util;

public class QueuedThumbnailer {
    public static readonly QueuedThumbnailer Instance = new();

    private const float CropThresholdAspectRatioRatio = 2;

    private readonly Queue<Task<Task<Bitmap>>> _taskQueue = new();

    private QueuedThumbnailer() { }

    public Task<Bitmap> LoadFromTexStream(
        int w,
        int h,
        Stream stream,
        PlatformId platformId,
        CancellationToken cancellationToken = default) {
        Task<Task<Bitmap>> task;
        lock (_taskQueue) {
            _taskQueue.Enqueue(task = new(() => Task.Run(async () => {
                var f = await stream
                    .ExtractMipmapOfSizeAtLeast(Math.Max(w, h), platformId, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                f = f.Filter(format: TexFile.TextureFormat.B8G8R8A8);

                cancellationToken.ThrowIfCancellationRequested();

                Bitmap sourceBitmap;
                unsafe {
                    fixed (void* p = f.RawData) {
                        sourceBitmap = new(f.Width, f.Height, 4 * f.Width, PixelFormat.Format32bppArgb, (nint) p);
                    }
                }

                if (f.Width == w && f.Height == h)
                    return sourceBitmap;

                Bitmap? targetBitmap = null;
                try {
                    using var g = Graphics.FromImage(targetBitmap = new(w, h, PixelFormat.Format32bppArgb));
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
                    targetBitmap?.Dispose();
                    sourceBitmap.Dispose();
                }
            }, cancellationToken)));
        }

        ProcessQueuedItems();

        return task.Unwrap();
    }

    private void ProcessQueuedItems() {
        lock (_taskQueue) {
            if (_taskQueue.Count > Environment.ProcessorCount)
                return;

            if (!_taskQueue.TryDequeue(out var task))
                return;

            task.Unwrap().ContinueWith(_ => ProcessQueuedItems());
            task.Start(TaskScheduler.Default);
        }
    }
}
