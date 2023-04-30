using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0x50)]
[InputId(InputId.InstanceParameter)]
public struct InstanceParameter {
    [FieldOffset(0x00)] public Vector4 MulColor;
    [FieldOffset(0x10)] public Vector4 EnvParameter;

    [FieldOffset(0x20)] public CameraLightStruct CameraLight;
    [FieldOffset(0x40)] public Vector4 Wetness;

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public struct CameraLightStruct {
        [FieldOffset(0x00)] public Vector4 DiffuseSpecular;
        [FieldOffset(0x10)] public Vector4 Rim;
    }
}