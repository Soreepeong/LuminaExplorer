using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x60)]
[InputId(InputId.BackgroundInstanceData)]
public struct BackgroundInstanceData {
    [FieldOffset(0x00)] public Matrix3X4<float> TransformMatrix;
    [FieldOffset(0x30)] public Vector4 InstanceParam0;
    [FieldOffset(0x40)] public Vector4 InstanceParam1;
    [FieldOffset(0x50)] public Vector4 InstanceParam2;
}