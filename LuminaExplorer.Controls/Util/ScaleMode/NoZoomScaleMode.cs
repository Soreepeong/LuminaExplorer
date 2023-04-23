using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public readonly struct NoZoomScaleMode : IScaleMode {
    public NoZoomScaleMode() { }

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) => 1f;

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) => 0f;
}
