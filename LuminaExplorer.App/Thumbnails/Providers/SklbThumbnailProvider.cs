using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Skeletons: the bind pose drawn as a stick figure in a 3/4 view.</summary>
/// <remarks>Partial skeletons (face, hair, ...) are drawn highlighted over a faint base skeleton of the same model.
/// </remarks>
public sealed partial class SklbThumbnailProvider : IThumbnailProvider {
    private const float Yaw = 35 * MathF.PI / 180;
    private const float Pitch = 12 * MathF.PI / 180;

    private static readonly Color BackgroundTop = Color.FromArgb(38, 45, 60);
    private static readonly Color BackgroundBottom = Color.FromArgb(22, 26, 35);
    private static readonly Color MainNear = Color.FromArgb(150, 215, 255);
    private static readonly Color MainFar = Color.FromArgb(55, 95, 145);
    private static readonly Color PartialNear = Color.FromArgb(255, 190, 90);
    private static readonly Color PartialFar = Color.FromArgb(160, 95, 40);
    private static readonly Color ContextNear = Color.FromArgb(110, 150, 165, 185);
    private static readonly Color ContextFar = Color.FromArgb(45, 150, 165, 185);

    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Medium;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<SklbFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var sklb = await request.Lookup.AsFileResource<SklbFile>(cancellationToken);
        if (sklb.LoadException is not null || sklb.Bones.Length == 0)
            return null;

        SklbFile? baseSklb = null;
        string? partKind = null;
        if (request.FullPath is { } fullPath &&
            SkeletonPathRegex().Match(fullPath.TrimStart('/')) is { Success: true } match) {
            partKind = match.Groups["kind"].Value.ToLowerInvariant();
            if (partKind != "base") {
                var model = match.Groups["model"].Value.ToLowerInvariant();
                var basePath =
                    $"chara/{match.Groups["category"].Value.ToLowerInvariant()}/{model}/skeleton/base/b0001/skl_{model}b0001.sklb";
                try {
                    baseSklb = await context.GetSklb(basePath, cancellationToken);
                } catch (Exception e) when (e is not OperationCanceledException) {
                    baseSklb = null;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var mainPositions = ComputePositions(sklb, baseSklb);
        var isPartial = baseSklb is not null;
        var segments = new List<Segment>();
        var fitPoints = new List<Vector3>();

        if (isPartial) {
            var basePositions = baseSklb!.Bones.Select(x => x.BindPoseAbsolute.Translation).ToArray();
            foreach (var bone in baseSklb.Bones) {
                if (bone.Parent is { } parent)
                    segments.Add(new(basePositions[parent.Index], basePositions[bone.Index], SegmentKind.Context));
            }
        }

        foreach (var bone in sklb.Bones) {
            fitPoints.Add(mainPositions[bone.Index]);
            if (bone.Parent is { } parent) {
                segments.Add(
                    new(
                        mainPositions[parent.Index],
                        mainPositions[bone.Index],
                        isPartial ? SegmentKind.Partial : SegmentKind.Main));
            }
        }

        var bitmap = Draw(
            segments,
            fitPoints,
            isPartial,
            request.Settings,
            isPartial ? $"{partKind} · {sklb.Bones.Length} bones" : $"{sklb.Bones.Length} bones");
        return new(bitmap, ThumbnailKind.Render);
    }

    /// <summary>Computes the model space positions of bones of a (possibly partial) skeleton.</summary>
    /// <remarks>Bones that also exist in the base skeleton use the position from there; others are positioned
    /// relative to their parents.</remarks>
    private static Vector3[] ComputePositions(SklbFile sklb, SklbFile? baseSklb)
    {
        var matrices = new Matrix4x4[sklb.Bones.Length];
        var baseByName = baseSklb?.Bones
            .GroupBy(x => x.Name)
            .ToDictionary(x => x.Key, x => x.First());
        foreach (var bone in sklb.Bones) {
            if (baseByName?.TryGetValue(bone.Name, out var baseBone) is true)
                matrices[bone.Index] = baseBone.BindPoseAbsolute;
            else if (bone.Parent is { } parent)
                matrices[bone.Index] = bone.BindPoseRelative * matrices[parent.Index];
            else
                matrices[bone.Index] = bone.BindPoseRelative;
        }

        return matrices.Select(x => x.Translation).ToArray();
    }

    private static Bitmap Draw(
        List<Segment> segments,
        List<Vector3> fitPoints,
        bool isPartial,
        ThumbnailSettings settings,
        string caption)
    {
        var width = settings.Width;
        var height = settings.Height;
        var size = Math.Min(width, height);

        var view = Matrix4x4.CreateRotationY(Yaw) * Matrix4x4.CreateRotationX(Pitch);
        Vector3 Project(Vector3 p) => Vector3.Transform(p, view);

        // Fit the main bones; the base skeleton of partial skeletons is only there for context.
        var projected = fitPoints.Select(Project).ToArray();
        var minX = projected.Min(x => x.X);
        var maxX = projected.Max(x => x.X);
        var minY = projected.Min(x => x.Y);
        var maxY = projected.Max(x => x.Y);
        var extent = MathF.Max(maxX - minX, maxY - minY);
        if (isPartial) {
            // Show some surroundings, so that the location of the partial skeleton is apparent.
            var grow = MathF.Max(extent * 0.35f, 0.08f);
            (minX, maxX, minY, maxY) = (minX - grow, maxX + grow, minY - grow, maxY + grow);
        }

        if (extent < 1e-4f) {
            (minX, maxX, minY, maxY) = (minX - 0.5f, maxX + 0.5f, minY - 0.5f, maxY + 0.5f);
        }

        var padding = size * 0.08f;
        var scale = MathF.Min((width - padding * 2) / (maxX - minX), (height - padding * 2) / (maxY - minY));
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        PointF ToScreen(Vector3 p) => new(width / 2f + (p.X - cx) * scale, height / 2f - (p.Y - cy) * scale);

        var minZ = float.MaxValue;
        var maxZ = float.MinValue;
        var drawables = new List<(PointF A, PointF B, float Depth, SegmentKind Kind)>(segments.Count);
        foreach (var s in segments) {
            var a = Project(s.A);
            var b = Project(s.B);
            if (Vector3.DistanceSquared(a, b) < 1e-10f)
                continue;
            var depth = (a.Z + b.Z) / 2;
            if (s.Kind != SegmentKind.Context) {
                minZ = MathF.Min(minZ, depth);
                maxZ = MathF.Max(maxZ, depth);
            }

            drawables.Add((ToScreen(a), ToScreen(b), depth, s.Kind));
        }

        if (minZ > maxZ)
            (minZ, maxZ) = (-1, 1);

        // Far first.
        drawables.Sort((x, y) => x.Depth.CompareTo(y.Depth));

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var brush = new LinearGradientBrush(
                       new Point(0, 0),
                       new Point(0, height),
                       BackgroundTop,
                       BackgroundBottom))
                g.FillRectangle(brush, 0, 0, width, height);

            var lineWidth = MathF.Max(1f, size / 110f);
            var jointRadius = lineWidth * 1.1f;
            foreach (var (a, b, depth, kind) in drawables) {
                var t = maxZ - minZ < 1e-6f ? 1f : Math.Clamp((depth - minZ) / (maxZ - minZ), 0, 1);
                var color = kind switch {
                    SegmentKind.Context => Lerp(ContextFar, ContextNear, t),
                    SegmentKind.Partial => Lerp(PartialFar, PartialNear, t),
                    _ => Lerp(MainFar, MainNear, t),
                };
                var w = kind == SegmentKind.Context ? lineWidth * 0.8f : lineWidth * (0.75f + 0.5f * t);
                using var pen = new Pen(color, w);
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLine(pen, a, b);

                if (kind != SegmentKind.Context) {
                    using var brush = new SolidBrush(color);
                    g.FillEllipse(brush, b.X - jointRadius, b.Y - jointRadius, jointRadius * 2, jointRadius * 2);
                }
            }

            if (size >= 96) {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var font = InfoCard.CreateFont(MathF.Max(8 * settings.DpiScale, size * 0.05f), FontStyle.Regular);
                using var brush = new SolidBrush(Color.FromArgb(190, 210, 220, 235));
                g.DrawString(caption, font, brush, size * 0.03f, size * 0.025f);
            }

            return bitmap;
        } catch (Exception) {
            bitmap.Dispose();
            throw;
        }
    }

    private static Color Lerp(Color a, Color b, float t) => Color.FromArgb(
        (int) (a.A + (b.A - a.A) * t),
        (int) (a.R + (b.R - a.R) * t),
        (int) (a.G + (b.G - a.G) * t),
        (int) (a.B + (b.B - a.B) * t));

    [GeneratedRegex(
        @"^chara/(?<category>[a-z]+)/(?<model>[a-z]\d{4})/skeleton/(?<kind>[a-z]+)/[a-z]\d{4}/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkeletonPathRegex();

    private enum SegmentKind {
        Main,
        Partial,
        Context,
    }

    private readonly record struct Segment(Vector3 A, Vector3 B, SegmentKind Kind);
}
