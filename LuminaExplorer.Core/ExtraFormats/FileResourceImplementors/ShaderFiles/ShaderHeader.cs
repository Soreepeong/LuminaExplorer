namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderHeader {
    public uint BytecodeOffset;
    public uint BytecodeSize;
    public ushort NumConstants;
    public ushort NumSamplers;
    public ushort NumUnknown1;
    public ushort NumUnknown2;

    public int NumInputs => NumConstants + NumSamplers + NumUnknown1 + NumUnknown2;

    public override string ToString() {
        if (NumUnknown1 == 0 && NumUnknown2 == 0)
            return $"C={NumConstants} S={NumSamplers}";
        else
            return $"C={NumConstants} S={NumSamplers} ?={NumUnknown1} ??={NumUnknown2}";
    }
}