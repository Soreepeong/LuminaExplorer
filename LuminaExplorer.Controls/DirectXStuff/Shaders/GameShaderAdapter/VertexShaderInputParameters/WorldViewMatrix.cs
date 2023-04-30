using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x30)]
[InputId(InputId.WorldViewMatrix)]
public struct WorldViewMatrix {
    [FieldOffset(0)] public Matrix3X4<float> Value;
}