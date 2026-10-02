using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Structs;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Hardware cursors (common/hardwarecursor/*.hwc): raw 64x64 B8G8R8A8 pixels.</summary>
/// <remarks>Cursors are mostly white and only use the top left part of the image, so the used part is shown enlarged
/// on a dark tile.</remarks>
public sealed class HwcThumbnailProvider : IThumbnailProvider {
    private const int Edge = 64;
    private const int DataSize = Edge * Edge * 4;

    private static readonly Color Background = Color.FromArgb(52, 56, 64);

    public int Priority => 100;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) =>
        request is { Extension: ".hwc", PackType: FileType.Standard, Size: DataSize };

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var data = await request.Lookup.ReadAll(cancellationToken);
        if (data.Length != DataSize)
            return null;

        // Bounds of the visible pixels.
        int left = Edge, top = Edge, right = -1, bottom = -1;
        for (var y = 0; y < Edge; y++) {
            for (var x = 0; x < Edge; x++) {
                if (data[(y * Edge + x) * 4 + 3] == 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0)
            (left, top, right, bottom) = (0, 0, Edge - 1, Edge - 1);

        using var source = new Bitmap(Edge, Edge, PixelFormat.Format32bppArgb);
        var lb = source.LockBits(new(0, 0, Edge, Edge), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try {
            for (var y = 0; y < Edge; y++)
                Marshal.Copy(data, y * Edge * 4, lb.Scan0 + y * lb.Stride, Edge * 4);
        } finally {
            source.UnlockBits(lb);
        }

        var size = Math.Min(request.Width, request.Height);
        var contentWidth = right - left + 1;
        var contentHeight = bottom - top + 1;
        var available = size * 0.8f;
        var scale = Math.Min(available / contentWidth, available / contentHeight);
        if (scale >= 1)
            scale = MathF.Floor(scale);

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try {
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Background);
            g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : request.Settings.InterpolationMode;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var w = contentWidth * scale;
            var h = contentHeight * scale;
            g.DrawImage(
                source,
                new RectangleF((size - w) / 2, (size - h) / 2, w, h),
                new(left, top, contentWidth, contentHeight),
                GraphicsUnit.Pixel);
            return new(bitmap, ThumbnailKind.Image);
        } catch (Exception) {
            bitmap.Dispose();
            throw;
        }
    }
}
