using Silk.NET.Maths;

namespace LuminaExplorer.Controls.Util;

public static class SilkExtensions {
    public static Box2D<float> ToSilkFloat(this System.Drawing.Rectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

    public static Box2D<float> ToSilkFloat(this RectangleF rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
}
