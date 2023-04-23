using System;
using System.Drawing;

namespace LuminaExplorer.Controls.Util.ScaleMode;

public interface IScaleMode {
    float CalcZoom(SizeF content, SizeF client, int exponentUnit);
    
    float CalcZoomExponent(SizeF content, SizeF client, int exponentUnit);

    SizeF CalcSize(SizeF content, SizeF client, int exponentUnit) {
        var zoom = CalcZoom(content, client, exponentUnit);
        return new(content.Width * zoom, content.Height * zoom);
    }

    public static bool ContentFitsIn(SizeF content, SizeF client) =>
        content.Width <= client.Width && content.Height <= client.Height;

    public static float ZoomToExponent(float zoom, int exponentUnit) => MathF.Log2(zoom) * exponentUnit;
    public static float ExponentToZoom(float exponent, int exponentUnit) => MathF.Pow(2, 1f * exponent / exponentUnit);
}