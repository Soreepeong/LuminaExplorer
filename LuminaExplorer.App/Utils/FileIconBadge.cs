using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LuminaExplorer.App.Utils;

/// <summary>
/// Creates file icons with the file extension drawn as a badge on the bottom right corner, like archivers do.
/// </summary>
public sealed class FileIconBadge : IDisposable {
    private const int MaxExtensionLength = 4;

    private readonly Func<int, Image?> _baseIconGetter;
    private readonly Dictionary<(string Extension, int Size), Bitmap> _cache = new();

    /// <param name="baseIconGetter">Gets the plain file icon closest to the requested size in pixels.</param>
    public FileIconBadge(Func<int, Image?> baseIconGetter)
    {
        this._baseIconGetter = baseIconGetter;
    }

    public void Dispose()
    {
        foreach (var b in this._cache.Values)
            b.Dispose();
        this._cache.Clear();
    }

    /// <summary>
    /// Gets the file icon with the extension of <paramref name="fileName"/> drawn on it, or null if there is no
    /// extension. The returned bitmap is owned by this object.
    /// </summary>
    public Bitmap? Get(string fileName, int size)
    {
        var extension = GetBadgeText(fileName);
        if (extension is null)
            return null;

        // Small icons only have room for a few letters.
        if (size < 24 && extension.Length > 3)
            extension = extension[..3];

        if (this._cache.TryGetValue((extension, size), out var bitmap))
            return bitmap;

        bitmap = new(size, size);
        using (var g = Graphics.FromImage(bitmap)) {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (this._baseIconGetter(size) is { } baseIcon)
                g.DrawImage(baseIcon, 0, 0, size, size);
            DrawBadgeText(g, new(0, 0, size, size), extension, size);
        }

        this._cache.Add((extension, size), bitmap);
        return bitmap;
    }

    /// <summary>Gets the text to show on the badge, or null if the file has no extension.</summary>
    public static string? GetBadgeText(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        if (dot < 0 || dot == fileName.Length - 1 || fileName.IndexOf('/', dot) >= 0)
            return null;

        var extension = fileName[(dot + 1)..].ToUpperInvariant();
        return extension.Length > MaxExtensionLength ? extension[..MaxExtensionLength] : extension;
    }

    /// <summary>
    /// Draws the extension badge of <paramref name="fileName"/> on the bottom right corner of
    /// <paramref name="bounds"/>, sized as it would be on an icon of <paramref name="referenceSize"/> pixels.
    /// </summary>
    public static void DrawBadge(Graphics g, Rectangle bounds, string fileName, int referenceSize)
    {
        if (GetBadgeText(fileName) is not { } text)
            return;

        var smoothingMode = g.SmoothingMode;
        try {
            DrawBadgeText(g, bounds, text, Math.Min(referenceSize, Math.Max(bounds.Width, bounds.Height)));
        } finally {
            g.SmoothingMode = smoothingMode;
        }
    }

    private static void DrawBadgeText(Graphics g, Rectangle iconBounds, string text, int referenceSize)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Lay out the text as an outline, so that it can be centered by the actual glyph shapes rather than by font
        // metrics; vertical placement uses the cap height, so that every badge puts its text at the same height.
        var family = FontFamily.GenericSansSerif;
        var emSize = Math.Max(8f, referenceSize * 0.34f);
        using var textPath = new GraphicsPath();
        textPath.AddString(text, family, (int) FontStyle.Bold, emSize, PointF.Empty, StringFormat.GenericTypographic);
        using var capPath = new GraphicsPath();
        capPath.AddString("H", family, (int) FontStyle.Bold, emSize, PointF.Empty, StringFormat.GenericTypographic);
        var textBounds = textPath.GetBounds();
        var capBounds = capPath.GetBounds();

        var paddingX = capBounds.Height * 0.6f;
        var paddingY = capBounds.Height * 0.45f;
        var width = textBounds.Width + paddingX * 2;
        var height = capBounds.Height + paddingY * 2;

        // Shrink to fit the icon; long text is condensed horizontally only, so that all badges have the same height.
        var scaleY = Math.Min(1f, Math.Min(iconBounds.Height, referenceSize) * 0.6f / height);
        var scaleX = Math.Min(scaleY, iconBounds.Width / width);
        width *= scaleX;
        height = MathF.Round(height * scaleY);
        var rect = new RectangleF(iconBounds.Right - width, iconBounds.Bottom - height, width, height);

        using (var capsule = RoundedRectangle(rect, height / 2))
        using (var brush = new SolidBrush(GetColor(text)))
        using (var pen = new Pen(Color.FromArgb(160, Color.White), Math.Max(1f, height / 12))) {
            g.FillPath(brush, capsule);
            g.DrawPath(pen, capsule);
        }

        using var transform = new Matrix();
        transform.Translate(
            rect.Left + (rect.Width - textBounds.Width * scaleX) / 2,
            rect.Top + (rect.Height - capBounds.Height * scaleY) / 2);
        transform.Scale(scaleX, scaleY);
        transform.Translate(-textBounds.Left, -capBounds.Top);
        textPath.Transform(transform);
        g.FillPath(Brushes.White, textPath);
    }

    private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0) {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Picks a stable, readable background color for an extension.</summary>
    public static Color GetColor(string extension)
    {
        var hash = 0u;
        foreach (var c in extension)
            hash = hash * 31 + c;

        // Dark enough for white text.
        var hue = hash % 360 / 60f;
        const float chroma = 0.55f;
        const float lightness = 0.38f;
        var x = chroma * (1 - Math.Abs(hue % 2 - 1));
        var (r, g, b) = (int) hue switch {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        var m = lightness - chroma / 2;
        return Color.FromArgb(
            (int) Math.Clamp((r + m) * 255, 0, 255),
            (int) Math.Clamp((g + m) * 255, 0, 255),
            (int) Math.Clamp((b + m) * 255, 0, 255));
    }
}
