using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Thumbnails of textures (.tex, .atex, .dds) and images supported by WIC.</summary>
public sealed class TexThumbnailProvider : IThumbnailProvider {
    public int Priority => 10;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) =>
        IsDefinitelyTexture(request) ||
        ImagingExtensions.ThumbnailSupportedExtensions.Any(
            x => request.Name.EndsWith(x, StringComparison.InvariantCultureIgnoreCase)) ||
        // may be an .atex file, which has no magic
        (!request.NameResolved && request is { PackType: FileType.Standard, Size: > 256, MagicType: null });

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var w = request.Width;
        var h = request.Height;

        Bitmap? sourceBitmap = null;
        Bitmap? targetBitmap = null;
        try {
            await using (var stream = request.Lookup.CreateStream()) {
                if (IsDefinitelyTexture(request))
                    sourceBitmap = await stream.ExtractMipmapOfSizeAtLeastForTex(
                        Math.Max(w, h),
                        request.PlatformId,
                        cancellationToken);
                else
                    sourceBitmap = await stream.ExtractMipmapOfSizeAtLeast(
                        Math.Max(w, h),
                        request.PlatformId,
                        cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (sourceBitmap.Width <= w && sourceBitmap.Height <= h) {
                var result = new ThumbnailResult(sourceBitmap, ThumbnailKind.Image);
                sourceBitmap = null;
                return result;
            }

            targetBitmap = FitBitmap(
                sourceBitmap,
                w,
                h,
                request.Settings.CropThresholdAspectRatioRatio,
                request.Settings.InterpolationMode);
            var res = new ThumbnailResult(targetBitmap, ThumbnailKind.Image);
            targetBitmap = null;
            return res;
        } finally {
            sourceBitmap?.Dispose();
            targetBitmap?.Dispose();
        }
    }

    private static bool IsDefinitelyTexture(ThumbnailRequest request) =>
        request.PackType == FileType.Texture || request.Extension is ".tex" or ".atex";

    /// <summary>Scales down a bitmap to fit in the given size, cropping the center of overly long images.</summary>
    /// <param name="sourceBitmap">Bitmap to scale.</param>
    /// <param name="w">Target width.</param>
    /// <param name="h">Target height.</param>
    /// <param name="cropThresholdAspectRatioRatio">Images whose aspect ratio differs from that of the target size by
    /// more than this ratio get cropped.</param>
    /// <param name="interpolationMode">Interpolation mode.</param>
    public static Bitmap FitBitmap(
        Bitmap sourceBitmap,
        int w,
        int h,
        float cropThresholdAspectRatioRatio,
        System.Drawing.Drawing2D.InterpolationMode interpolationMode)
    {
        var srcRect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);

        var sourceAspectRatio = (float) sourceBitmap.Height / sourceBitmap.Width;
        var targetAspectRatio = (float) w / h;
        if (sourceAspectRatio < targetAspectRatio) {
            // horizontally wider
            if (sourceAspectRatio < targetAspectRatio / cropThresholdAspectRatioRatio) {
                sourceAspectRatio = targetAspectRatio / cropThresholdAspectRatioRatio;
                srcRect.Width = (int) (sourceBitmap.Height / sourceAspectRatio);
                srcRect.X = (sourceBitmap.Width - srcRect.Width) / 2;
            }

            // fit height
            h = (int) (w * sourceAspectRatio);
        } else {
            // vertically wider
            if (sourceAspectRatio > targetAspectRatio * cropThresholdAspectRatioRatio) {
                sourceAspectRatio = targetAspectRatio * cropThresholdAspectRatioRatio;
                srcRect.Height = (int) (sourceBitmap.Width * sourceAspectRatio);
                srcRect.Y = (sourceBitmap.Height - srcRect.Height) / 2;
            }

            // fit width
            w = (int) (h / sourceAspectRatio);
        }

        var targetBitmap = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        try {
            using var g = Graphics.FromImage(targetBitmap);
            g.InterpolationMode = interpolationMode;
            g.DrawImage(sourceBitmap, new Rectangle(0, 0, w, h), srcRect, GraphicsUnit.Pixel);
            return targetBitmap;
        } catch (Exception) {
            targetBitmap.Dispose();
            throw;
        }
    }
}
