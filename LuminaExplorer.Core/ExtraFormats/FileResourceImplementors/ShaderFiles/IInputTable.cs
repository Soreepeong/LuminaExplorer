namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public interface IInputTable {
    public InputId InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public ushort InputStringSize { get; set; }
    public ushort IsTexture { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }
}
