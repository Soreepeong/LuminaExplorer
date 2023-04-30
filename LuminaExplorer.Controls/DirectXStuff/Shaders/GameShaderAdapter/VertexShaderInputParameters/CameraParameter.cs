using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x1C0)]
[InputId(InputId.CameraParameter)]
public struct CameraParameter {
    [FieldOffset(0x000)] public Matrix3X4<float> ViewMatrix;
    [FieldOffset(0x030)] public Matrix3X4<float> InverseViewMatrix;
    [FieldOffset(0x060)] public Matrix4X4<float> ViewProjectionMatrix;
    [FieldOffset(0x0A0)] public Matrix4X4<float> InverseViewProjectionMatrix;
    [FieldOffset(0x0E0)] public Matrix4X4<float> InverseProjectionMatrix;
    [FieldOffset(0x120)] public Matrix4X4<float> ProjectionMatrix;
    [FieldOffset(0x160)] public Matrix4X4<float> MainViewToProjectionMatrix;
    [FieldOffset(0x1A0)] public Vector3D<float> EyePosition;
    [FieldOffset(0x1B0)] public Vector3D<float> LookAtVector;

    public static CameraParameter FromViewProjection(Matrix4x4 view, Matrix4x4 projection) {
        var viewProjectionMatrix = Matrix4x4.Multiply(view, projection);
        return new() {
            ViewMatrix = view.TruncateAs3X4ToSilkValue(),
            InverseViewMatrix = (Matrix4x4.Invert(view, out var inverseView)
                ? inverseView
                : Matrix4x4.Identity).TruncateAs3X4ToSilkValue(),
            ViewProjectionMatrix = viewProjectionMatrix.ToSilkValue(),
            InverseViewProjectionMatrix = (Matrix4x4.Invert(viewProjectionMatrix, out var inverseViewProjection)
                ? inverseViewProjection
                : Matrix4x4.Identity).ToSilkValue(),
            ProjectionMatrix = projection.ToSilkValue(),
            InverseProjectionMatrix = (Matrix4x4.Invert(projection, out var inverseProjection)
                ? inverseProjection
                : Matrix4x4.Identity).ToSilkValue(),
            MainViewToProjectionMatrix = Matrix4X4<float>.Identity, // idk
            EyePosition = inverseView.Translation.ToSilkValue(),
            LookAtVector = Vector3D<float>.One // idk
        };
    }
}
