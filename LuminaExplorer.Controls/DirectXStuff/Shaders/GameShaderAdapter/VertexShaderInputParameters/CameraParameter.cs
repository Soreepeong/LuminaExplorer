using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x1BC)]
[InputId(InputId.CameraParameter)]
public struct CameraParameter {
    [FieldOffset(0x000)] public Matrix3X4<float> ViewMatrix;
    [FieldOffset(0x030)] public Matrix3X4<float> InverseViewMatrix;
    [FieldOffset(0x060)] public Matrix4X4<float> ViewProjectionMatrix;
    [FieldOffset(0x0a0)] public Matrix4X4<float> InverseViewProjectionMatrix;
    [FieldOffset(0x0e0)] public Matrix4X4<float> InverseProjectionMatrix;
    [FieldOffset(0x120)] public Matrix4X4<float> ProjectionMatrix;
    [FieldOffset(0x160)] public Matrix4X4<float> MainViewToProjectionMatrix;
    [FieldOffset(0x1A0)] public Vector3 EyePosition;
    [FieldOffset(0x1B0)] public Vector3 LookAtVector;
}