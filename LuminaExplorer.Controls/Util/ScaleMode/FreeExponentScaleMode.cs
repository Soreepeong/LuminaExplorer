using System;
using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public struct FreeExponentScaleMode : IScaleMode {
    public int ZoomExponent { get; set; }

    public FreeExponentScaleMode(int exponent) => ZoomExponent = exponent;

    public float CalcZoom(SizeF content, SizeF client, int exponentUnit) =>
        IScaleMode.ExponentToZoom(ZoomExponent, exponentUnit);

    public float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit) => ZoomExponent;
}