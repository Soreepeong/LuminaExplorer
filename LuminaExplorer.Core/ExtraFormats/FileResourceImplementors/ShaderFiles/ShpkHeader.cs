using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[StructLayout(LayoutKind.Sequential)]
public struct ShpkHeader {
    public const uint MagicValue = 0x6b506853;

    public uint Magic;
    public uint Version;
    public DirectXVersion DirectXVersion;
    public uint FileSize;
    public uint BlobOffset;
    public uint StringsOffset;
    public uint VertexShaderCount;
    public uint PixelShaderCount;
    public uint MaterialParamSize;
    public ushort MaterialParamCount;
    public ushort HasMaterialParamDefaults;
    public uint ConstantCount;
    public ushort SamplerCount;
    public ushort TextureCount;
    public uint UavCount;
    public uint SystemKeyCount;
    public uint SceneKeyCount;
    public uint MaterialKeyCount;
    public uint NodeCount;
    public uint NodeAliasCount;

    public override string ToString() =>
        $"{this.DirectXVersion}: V={this.VertexShaderCount} P={this.PixelShaderCount} H1={this.MaterialParamCount} U1={this.MaterialParamSize} " +
        $"NSP={this.ConstantCount} NRP={this.SamplerCount}";
}
