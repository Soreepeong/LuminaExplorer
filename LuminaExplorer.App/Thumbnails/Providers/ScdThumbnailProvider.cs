using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Core.Audio;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Sound containers: format of the first playable entry, and a waveform for cheaply decodable codecs.
/// </summary>
public sealed class ScdThumbnailProvider : IThumbnailProvider {
    private const long MaxSamplesForWaveform = 16L << 20;

    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Medium;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<ScdFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var scd = await request.Lookup.AsFileResource<ScdFile>(cancellationToken);
        var entries = ScdAudioEntry.ReadAll(scd, cancellationToken);
        var card = new InfoCard("SCD", request.DisplayStem);

        if ((entries.FirstOrDefault(x => x.IsPlayable) ?? entries.FirstOrDefault()) is not { } entry) {
            return card
                .AddLine("No audio", true)
                .Render(request.Settings);
        }

        card.AddLine(entry.CodecName, true);
        card.AddLine($"{entry.Channels}ch · {entry.SampleRate / 1000f:0.#} kHz");
        if (entry.TotalFrames >= 0)
            card.AddLine(InfoCard.FormatDuration(entry.Duration) + (entry.HasLoop ? " · loop" : ""));
        if (entries.Count > 1)
            card.AddLine($"×{entries.Count} entries");
        if (!entry.IsPlayable)
            card.AddLine(entry.UnsupportedReason);

        if (entry.IsPlayable && entry.TotalFrames > 0) {
            float[]? envelope = null;
            if (entry.Codec is ScdAudioCodec.MsAdpcm or ScdAudioCodec.Pcm16 &&
                entry.TotalFrames * entry.Channels <= MaxSamplesForWaveform) {
                cancellationToken.ThrowIfCancellationRequested();
                envelope = ComputeEnvelope(entry.DecodeToPcm16(), entry.Channels, Math.Max(16, request.Width));
            }

            var accent = card.Accent;
            card.Visual = (g, rect) => DrawTimeline(g, rect, entry, envelope, accent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return card.Render(request.Settings);
    }

    /// <summary>Computes the peak amplitude (0 to 1) of each of <paramref name="columns"/> equal parts.</summary>
    private static float[] ComputeEnvelope(short[] samples, int channels, int columns)
    {
        var frames = samples.Length / channels;
        var res = new float[columns];
        for (var c = 0; c < columns; c++) {
            var from = (int) ((long) frames * c / columns);
            var to = Math.Max(from + 1, (int) ((long) frames * (c + 1) / columns));
            var peak = 0;
            for (var i = from * channels; i < to * channels && i < samples.Length; i++)
                peak = Math.Max(peak, Math.Abs((int) samples[i]));
            res[c] = peak / 32768f;
        }

        return res;
    }

    private static void DrawTimeline(Graphics g, RectangleF rect, ScdAudioEntry entry, float[]? envelope, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(Color.FromArgb(242, 244, 247)))
            g.FillRectangle(background, rect);

        if (entry.GetLoopTuple() is var (loopStart, loopEnd) && entry.TotalFrames > 0) {
            var x0 = rect.Left + rect.Width * loopStart / entry.TotalFrames;
            var x1 = rect.Left + rect.Width * loopEnd / entry.TotalFrames;
            using (var loopBrush = new SolidBrush(Color.FromArgb(40, accent)))
                g.FillRectangle(loopBrush, x0, rect.Top, Math.Max(1, x1 - x0), rect.Height);
            using var loopPen = new Pen(Color.FromArgb(150, accent), 1);
            g.DrawLine(loopPen, x0, rect.Top, x0, rect.Bottom);
            g.DrawLine(loopPen, x1, rect.Top, x1, rect.Bottom);
        }

        var mid = rect.Top + rect.Height / 2;
        if (envelope is null) {
            // No waveform; just show the timeline.
            using var pen = new Pen(Color.FromArgb(160, accent), Math.Max(1, rect.Height / 10));
            g.DrawLine(pen, rect.Left, mid, rect.Right, mid);
            return;
        }

        using var path = new GraphicsPath();
        var points = new PointF[envelope.Length * 2];
        for (var i = 0; i < envelope.Length; i++) {
            var x = rect.Left + rect.Width * (i + 0.5f) / envelope.Length;
            var amp = Math.Max(0.5f, envelope[i] * rect.Height / 2);
            points[i] = new(x, mid - amp);
            points[^(i + 1)] = new(x, mid + amp);
        }

        path.AddPolygon(points);
        using var brush = new SolidBrush(Color.FromArgb(200, accent));
        g.FillPath(brush, path);
    }
}
