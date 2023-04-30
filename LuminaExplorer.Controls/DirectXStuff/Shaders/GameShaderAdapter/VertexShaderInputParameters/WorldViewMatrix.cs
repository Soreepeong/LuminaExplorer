using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x30)]
[InputId(InputId.WorldViewMatrix)]
public struct WorldViewMatrix {
    [FieldOffset(0)] public Matrix3X4<float> Value;

    public static WorldViewMatrix FromWorldView(Matrix4x4 world, Matrix4x4 view) =>
        new() {Value = Matrix4x4.Multiply(world, view).TruncateAs3X4ToSilkValue()};
}
