namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShpkHeader {
    public const uint MagicValue = 0x6b506853;

    public uint Magic;
    public uint Version;
    public DirectXVersion DirectXVersion;
    public uint FileSize;
    public uint ShaderBytecodeBlockOffset;
    public uint InputStringBlockOffset;
    public uint NumVertexShaders;
    public uint NumPixelShaders;
    public uint Unknown1;
    public uint Unknown2;
    public uint NumScalarParameters;
    public uint NumResourceParameteres;

    // or, these are parameters
    public uint Unknown3;
    public uint Unknown4;
    public uint Unknown5;
    public uint Unknown6;
    public uint Unknown7;
    public uint Unknown8;
    
    public override string ToString() =>
        $"{DirectXVersion}: V={NumVertexShaders} P={NumPixelShaders} U1={Unknown1} U2={Unknown2} " +
        $"NSP={NumScalarParameters} NRP={NumResourceParameteres}";
}