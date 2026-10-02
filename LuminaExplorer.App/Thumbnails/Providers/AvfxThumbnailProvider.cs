using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>VFX files: the first texture, or the first four textures in a grid, with the object counts.</summary>
public sealed class AvfxThumbnailProvider : IThumbnailProvider {
    private static readonly Color Background = Color.FromArgb(24, 24, 28);
    private static readonly Color CellBackground = Color.FromArgb(40, 40, 46);

    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Medium;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<AvfxFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var avfx = await request.Lookup.AsFileResource<AvfxFile>(cancellationToken);
        var stats = $"E{avfx.Emitters.Count} P{avfx.Particles.Count} T{avfx.Textures.Count}";

        var width = request.Width;
        var height = request.Height;
        var paths = avfx.Textures
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim('\0', ' ').Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        var cellEdge = paths.Length <= 1 ? Math.Max(width, height) : Math.Max(width, height) / 2;

        var textures = new List<Bitmap>();
        try {
            foreach (var path in paths) {
                if (await context.LoadTexture(path, cellEdge, cancellationToken) is { } texture) {
                    textures.Add(texture);
                    ShowAlphaMaskAsGrayscale(texture);
                }
            }

            var loaded = textures;
            if (loaded.Count == 0) {
                return new InfoCard("AVFX", request.DisplayStem)
                    .AddLine($"{avfx.Emitters.Count} emitters", true)
                    .AddLine($"{avfx.Particles.Count} particles")
                    .AddLine($"{avfx.Textures.Count} textures")
                    .AddLine($"{avfx.Models.Count} models")
                    .AddLine($"{avfx.Timelines.Count} timelines")
                    .Render(request.Settings);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try {
                using var g = Graphics.FromImage(bitmap);
                g.InterpolationMode = request.Settings.InterpolationMode;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Background);

                if (loaded.Count == 1) {
                    DrawFitted(g, loaded[0], new(0, 0, width, height));
                } else {
                    var gap = Math.Max(1, Math.Min(width, height) / 64);
                    var cw = (width - gap) / 2;
                    var ch = (height - gap) / 2;
                    using var cellBrush = new SolidBrush(CellBackground);
                    for (var i = 0; i < 4; i++) {
                        var cell = new Rectangle(i % 2 * (cw + gap), i / 2 * (ch + gap), cw, ch);
                        g.FillRectangle(cellBrush, cell);
                        if (i < loaded.Count)
                            DrawFitted(g, loaded[i], cell);
                    }
                }

                DrawStats(g, stats, width, height, request.Settings.DpiScale);
                return new(bitmap, ThumbnailKind.Image);
            } catch (Exception) {
                bitmap.Dispose();
                throw;
            }
        } finally {
            foreach (var t in textures)
                t.Dispose();
        }
    }

    /// <summary>Converts textures that only have content in the alpha channel (masks with constant color) into opaque
    /// grayscale images of the alpha channel, as they would be invisible otherwise.</summary>
    private static void ShowAlphaMaskAsGrayscale(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var lb = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try {
            var pixels = new int[lb.Width * lb.Height];
            for (var y = 0; y < lb.Height; y++)
                Marshal.Copy(lb.Scan0 + y * lb.Stride, pixels, y * lb.Width, lb.Width);

            int minRgb = 255, maxRgb = 0, minA = 255, maxA = 0;
            foreach (var p in pixels) {
                var a = (p >> 24) & 0xFF;
                minA = Math.Min(minA, a);
                maxA = Math.Max(maxA, a);
                if (a == 0)
                    continue;
                var l = Math.Max((p >> 16) & 0xFF, Math.Max((p >> 8) & 0xFF, p & 0xFF));
                minRgb = Math.Min(minRgb, l);
                maxRgb = Math.Max(maxRgb, l);
            }

            // Visible enough as is?
            if (maxA - minA < 32 || (maxRgb - minRgb >= 24 && maxRgb >= 64))
                return;

            for (var i = 0; i < pixels.Length; i++) {
                var a = (pixels[i] >> 24) & 0xFF;
                pixels[i] = unchecked((int) 0xFF000000) | (a << 16) | (a << 8) | a;
            }

            for (var y = 0; y < lb.Height; y++)
                Marshal.Copy(pixels, y * lb.Width, lb.Scan0 + y * lb.Stride, lb.Width);
        } finally {
            bitmap.UnlockBits(lb);
        }
    }

    private static void DrawFitted(Graphics g, Bitmap bitmap, Rectangle cell)
    {
        var scale = Math.Min((float) cell.Width / bitmap.Width, (float) cell.Height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        g.DrawImage(bitmap, cell.Left + (cell.Width - w) / 2, cell.Top + (cell.Height - h) / 2, w, h);
    }

    private static void DrawStats(Graphics g, string text, int width, int height, float dpiScale)
    {
        var size = Math.Min(width, height);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = InfoCard.CreateFont(MathF.Max(8 * dpiScale, size * 0.055f), FontStyle.Bold);
        var textSize = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
        var pad = MathF.Max(2, size * 0.02f);
        var rect = new RectangleF(pad, pad, textSize.Width + pad * 2, textSize.Height + pad);
        using (var path = InfoCard.RoundedRectangle(rect, rect.Height / 3))
        using (var brush = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            g.FillPath(brush, path);
        g.DrawString(text, font, Brushes.White, rect.Left + pad, rect.Top + pad / 2, StringFormat.GenericTypographic);
    }
}
