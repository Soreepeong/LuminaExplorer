using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public struct FreeScaleMode : IScaleMode {
    public float Zoom { get; set; }

    public FreeScaleMode(float zoom) => Zoom = zoom;

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) => Zoom;

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) =>
        IScaleMode.ZoomToExponent(Zoom, exponentUnit);
}
