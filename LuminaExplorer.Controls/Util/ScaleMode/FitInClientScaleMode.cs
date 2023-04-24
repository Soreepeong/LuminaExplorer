using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public readonly struct FitInClientScaleMode : IScaleModeWithZoomInToFit {
    public FitInClientScaleMode(bool zoomInToFit) {
        ZoomInToFit = zoomInToFit;
    }

    public bool ZoomInToFit { get; }

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) => CalcZoomStatic(content, client, ZoomInToFit);

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) =>
        CalcZoomExponentStatic(content, client, ZoomInToFit, exponentUnit);

    public static float CalcZoomStatic(SizeF content, SizeF client, bool zoomInToFit) =>
        content.IsEmpty || (!zoomInToFit && IScaleMode.ContentFitsIn(content, client))
            ? 1f
            : client.Width * content.Height > content.Width * client.Height
                ? 1f * client.Height / content.Height
                : 1f * client.Width / content.Width;

    public static float CalcZoomExponentStatic(SizeF content, SizeF client, bool zoomInToFit, int exponentUnit) =>
        IScaleMode.ZoomToExponent(CalcZoomStatic(content, client, zoomInToFit), exponentUnit);
}