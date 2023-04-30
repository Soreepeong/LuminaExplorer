namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public interface IInputTable {
    public InputId InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public uint InputStringSize { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }
}