using System.Drawing;
using System.Numerics;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.Util;

public static class SilkExtensions {
    public static Box2D<float> ToSilkValue(this System.Drawing.Rectangle rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

    public static Box2D<float> ToSilkValue(this RectangleF rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

    public static Matrix4X4<float> ToSilkValue(this Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
