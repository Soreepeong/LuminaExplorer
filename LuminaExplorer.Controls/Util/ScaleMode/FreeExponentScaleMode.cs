using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public struct FreeExponentScaleMode : IScaleMode {
    public int ZoomExponent { get; set; }

    public FreeExponentScaleMode(int exponent) => this.ZoomExponent = exponent;

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) =>
        IScaleMode.ExponentToZoom(this.ZoomExponent, exponentUnit);

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) => this.ZoomExponent;
}
