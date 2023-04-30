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

    public static Vector3D<float> ToSilkValue(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector4D<float> ToSilkValue(this Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    public static Matrix4X4<float> ToSilkValue(this Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    public static Matrix3X4<float> TruncateAs3X4ToSilkValue(this Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34);

    // if you need to use this think again about the memory layout
    // public static Matrix4X3<float> TruncateAs4X3ToSilkValue(this Matrix4x4 m) => new(
    //     m.M11, m.M12, m.M13,
    //     m.M21, m.M22, m.M23,
    //     m.M31, m.M32, m.M33,
    //     m.M41, m.M42, m.M43);
}
