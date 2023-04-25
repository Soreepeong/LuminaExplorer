using System.Drawing;
using DirectN;

namespace LuminaExplorer.Controls.Util;

public static class SilkExtensions {
    public static D2D_RECT_F ToSilkFloat(this Rectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

    public static D2D_RECT_F ToSilkFloat(this RectangleF rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
}
