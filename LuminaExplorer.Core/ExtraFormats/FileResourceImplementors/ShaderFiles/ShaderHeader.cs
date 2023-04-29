namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderHeader {
    public uint BytecodeOffset;
    public uint BytecodeSize;
    public ushort NumConstants;
    public ushort NumSamplers;
    public ushort NumUnknown1;
    public ushort NumUnknown2;

    public int NumInputs => NumConstants + NumSamplers + NumUnknown1 + NumUnknown2;

    public override string ToString() => $"C={NumConstants} S={NumSamplers} U1={NumUnknown1} U2={NumUnknown2}";
}