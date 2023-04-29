namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShcdHeader {
    public const uint MagicValue = 0x64436853;

    public uint Magic;
    public ShcdVersion Version;
    public ShaderType ShaderType;
    public DirectXVersion DirectXVersion;
    public uint FileSize;
    public uint ShaderBytecodeBlockOffset;
    public uint InputStringBlockOffset;

    public override string ToString() => $"{DirectXVersion}: {ShaderType}";
}