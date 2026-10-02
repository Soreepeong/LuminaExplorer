using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using LuminaExplorer.App.Utils;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>Draws a card with a title and lines of text, for files that have no natural image representation.
/// </summary>
/// <remarks>The bottom right corner is kept clear for the extension badge drawn over thumbnails.</remarks>
public sealed class InfoCard {
    private static readonly Color CardBackground = Color.FromArgb(252, 252, 253);
    private static readonly Color CardBorder = Color.FromArgb(200, 205, 211);
    private static readonly Color StrongText = Color.FromArgb(32, 36, 42);
    private static readonly Color NormalText = Color.FromArgb(88, 94, 104);

    private readonly List<(string Text, bool Strong)> _lines = new();

    /// <param name="accentKey">Badge text (such as "SHPK") to pick the accent color from, consistent with the
    /// extension badges.</param>
    /// <param name="title">Title shown in the header band.</param>
    public InfoCard(string accentKey, string title)
    {
        this.Accent = FileIconBadge.GetColor(accentKey.ToUpperInvariant());
        this.Title = title;
    }

    public Color Accent { get; set; }

    public string Title { get; set; }

    /// <summary>Gets or sets a short text shown large above the lines, such as "VS".</summary>
    public string? Glyph { get; set; }

    /// <summary>Gets or sets a function that draws a visualization (such as a waveform) in a band below the text.
    /// It is only drawn if there is enough room.</summary>
    public Action<Graphics, RectangleF>? Visual { get; set; }

    /// <summary>Gets or sets the height of the visualization band, relative to the card height.</summary>
    public float VisualHeightRatio { get; set; } = 0.24f;

    public InfoCard AddLine(string? text, bool strong = false)
    {
        if (!string.IsNullOrEmpty(text))
            this._lines.Add((text, strong));
        return this;
    }

    public ThumbnailResult Render(ThumbnailSettings settings) =>
        new(this.RenderBitmap(settings.Width, settings.Height, settings.DpiScale), ThumbnailKind.InfoCard);

    public Bitmap RenderBitmap(int width, int height, float dpiScale)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        dpiScale = Math.Max(1f, dpiScale);
        var size = Math.Min(width, height);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var margin = MathF.Max(1, MathF.Round(size * 0.03f));
            var card = new RectangleF(margin, margin, width - margin * 2 - 1, height - margin * 2 - 1);
            var radius = MathF.Max(2, size * 0.05f);

            var titlePx = MathF.Max(9 * dpiScale, size * 0.066f);
            var bodyPx = MathF.Max(8 * dpiScale, size * 0.054f);
            using var titleFont = CreateFont(titlePx, FontStyle.Bold);
            using var bodyFont = CreateFont(bodyPx, FontStyle.Regular);
            using var strongFont = CreateFont(bodyPx, FontStyle.Bold);
            using var format = new StringFormat(StringFormat.GenericTypographic) {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
                LineAlignment = StringAlignment.Center,
            };

            using var cardPath = RoundedRectangle(card, radius);
            using (var brush = new SolidBrush(CardBackground))
                g.FillPath(brush, cardPath);

            // Header band, clipped to the rounded card.
            var headerHeight = MathF.Round(titlePx * 1.7f);
            var padding = MathF.Max(2, MathF.Round(size * 0.045f));
            g.SetClip(cardPath);
            using (var brush = new SolidBrush(this.Accent))
                g.FillRectangle(brush, card.Left, card.Top, card.Width, headerHeight);
            g.ResetClip();

            g.DrawString(
                this.Title,
                titleFont,
                Brushes.White,
                new RectangleF(card.Left + padding, card.Top, card.Width - padding * 2, headerHeight),
                format);

            using (var pen = new Pen(CardBorder, MathF.Max(1, dpiScale * 0.75f)))
                g.DrawPath(pen, cardPath);

            // Keep the bottom right corner clear for the extension badge (as large as on a 32px icon).
            var badgeZone = new RectangleF(
                width - 46 * dpiScale,
                height - 18 * dpiScale,
                46 * dpiScale,
                18 * dpiScale);

            var x = card.Left + padding;
            var y = card.Top + headerHeight + padding * 0.6f;
            var bottom = card.Bottom - padding * 0.5f;
            var right = card.Right - padding;

            // Visualization band, above the badge zone.
            if (this.Visual is { } visual && height >= 80 * dpiScale) {
                var bandHeight = MathF.Round(card.Height * this.VisualHeightRatio);
                var bandBottom = MathF.Min(bottom, badgeZone.Top - padding * 0.25f);
                var band = new RectangleF(x, bandBottom - bandHeight, right - x, bandHeight);
                if (band.Top > y + bodyPx * 1.3f) {
                    var state = g.Save();
                    try {
                        g.SetClip(band);
                        visual(g, band);
                    } finally {
                        g.Restore(state);
                    }

                    bottom = band.Top - padding * 0.25f;
                }
            }

            if (this.Glyph is { } glyph) {
                var glyphPx = MathF.Max(bodyPx * 1.6f, size * 0.12f);
                using var glyphFont = CreateFont(glyphPx, FontStyle.Bold);
                var lineHeight = glyphPx * 1.25f;
                if (y + lineHeight <= bottom) {
                    using var brush = new SolidBrush(this.Accent);
                    g.DrawString(glyph, glyphFont, brush, new RectangleF(x, y, right - x, lineHeight), format);
                    y += lineHeight;
                }
            }

            var bodyLineHeight = bodyPx * 1.38f;
            using var strongBrush = new SolidBrush(StrongText);
            using var normalBrush = new SolidBrush(NormalText);
            foreach (var (text, strong) in this._lines) {
                if (y + bodyLineHeight > bottom + bodyLineHeight * 0.15f)
                    break;

                var lineRight = right;
                if (y + bodyLineHeight > badgeZone.Top)
                    lineRight = MathF.Min(lineRight, badgeZone.Left - padding * 0.5f);
                if (lineRight - x < bodyPx * 2)
                    break;

                g.DrawString(
                    text,
                    strong ? strongFont : bodyFont,
                    strong ? strongBrush : normalBrush,
                    new RectangleF(x, y, lineRight - x, bodyLineHeight),
                    format);
                y += bodyLineHeight;
            }

            return bitmap;
        } catch (Exception) {
            bitmap.Dispose();
            throw;
        }
    }

    public static Font CreateFont(float pixelSize, FontStyle style)
    {
        try {
            return new("Segoe UI", pixelSize, style, GraphicsUnit.Pixel);
        } catch (ArgumentException) {
            return new(FontFamily.GenericSansSerif, pixelSize, style, GraphicsUnit.Pixel);
        }
    }

    public static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
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

    /// <summary>Formats a number compactly, such as 1234567 as "1.23M".</summary>
    public static string FormatCount(long n) => n switch {
        >= 10_000_000 => $"{n / 1_000_000d:0.#}M",
        >= 1_000_000 => $"{n / 1_000_000d:0.##}M",
        >= 100_000 => $"{n / 1_000d:0}K",
        _ => n.ToString("N0"),
    };

    public static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int) t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
        : t.TotalSeconds >= 10 ? $"{(int) t.TotalMinutes}:{t.Seconds:00}"
        : $"{t.TotalSeconds:0.00}s";
}
