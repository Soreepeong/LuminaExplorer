namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct InputTable : IInputTable {
    public uint InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public uint InputStringSize { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }

    public override string ToString() => $"{InternalId:X08}: {RegisterIndex}..{RegisterIndex + RegisterCount}";
}