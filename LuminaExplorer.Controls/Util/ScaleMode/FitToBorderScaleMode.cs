using System;
using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public readonly struct FitToBorderScaleMode : IScaleMode {
    public FitToBorderScaleMode(bool zoomInToFit, Direction directionToFit) {
        ZoomInToFit = zoomInToFit;
        DirectionToFit = directionToFit;
    }

    public bool ZoomInToFit { get; }

    public Direction DirectionToFit { get; }

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) =>
        CalcZoomStatic(content, client, ZoomInToFit, DirectionToFit);

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) =>
        CalcZoomExponentStatic(content, client, ZoomInToFit, DirectionToFit, exponentUnit);

    public static float CalcZoomStatic(SizeF content, SizeF client, bool zoomInToFit, Direction direction) =>
        content.IsEmpty || (!zoomInToFit && IScaleMode.ContentFitsIn(content, client))
            ? 1f
            : direction switch {
                Direction.Horizontal => 1f * client.Width / content.Width,
                Direction.Vertical => 1f * client.Height / content.Height,
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
            };

    public static float CalcZoomExponentStatic(
        SizeF content,
        SizeF client,
        bool zoomInToFit,
        Direction direction,
        int exponentUnit) =>
        IScaleMode.ZoomToExponent(CalcZoomStatic(content, client, zoomInToFit, direction), exponentUnit);

    public enum Direction {
        Horizontal,
        Vertical,
    }
}