namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShpkHeader {
    public const uint MagicValue = 0x6b506853;

    public uint Magic;
    public uint Version;
    public DirectXVersion DirectXVersion;
    public uint FileSize;
    public uint ShaderBytecodeBlockOffset;
    public uint InputStringBlockOffset;
    public uint VertexShaderCount;
    public uint PixelShaderCount;
    public uint MaterialParamSize;
    public uint MaterialParamCount;
    public uint ConstantCount;
    public uint SamplerCount;
    public uint UavCount;
    public uint SystemKeyCount;
    public uint SceneKeyCount;
    public uint MaterialKeyCount;
    public uint NodeCount;
    public uint ItemCount;

    public override string ToString() =>
        $"{DirectXVersion}: V={VertexShaderCount} P={PixelShaderCount} H1={MaterialParamCount} U1={MaterialParamSize} " +
        $"NSP={ConstantCount} NRP={SamplerCount}";
}