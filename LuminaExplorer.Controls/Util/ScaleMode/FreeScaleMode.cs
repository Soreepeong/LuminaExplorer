using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public struct FreeScaleMode : IScaleMode {
    public float Zoom { get; set; }

    public FreeScaleMode(float zoom) => this.Zoom = zoom;

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) => this.Zoom;

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) =>
        IScaleMode.ZoomToExponent(this.Zoom, exponentUnit);
}
