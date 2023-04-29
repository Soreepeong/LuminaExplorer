namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct VertexShaderInputTable : IInputTable {
    public uint InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public uint InputStringSize { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }
    public byte Unknown1 { get; set; }
    public byte Unknown2 { get; set; }
    public byte Unknown3 { get; set; }
    public byte Unknown4 { get; set; }

    public override string ToString() => $"[V]{InternalId:X08}: {RegisterIndex}..{RegisterIndex + RegisterCount}";
}