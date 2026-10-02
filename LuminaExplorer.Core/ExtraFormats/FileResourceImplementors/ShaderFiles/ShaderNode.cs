namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderNode {
    /// <summary>Selector of this node; see <see cref="ShpkFile.BuildSelector(System.Collections.Generic.IEnumerable{uint})"/>.</summary>
    public uint Id;
    public byte[] PassIndices;

    /// <summary>
    /// Two unknown key values present since version 0x0D01.
    /// For older versions, this is a copy of <see cref="SubViewKeys"/>.
    /// </summary>
    public uint[] Unk131Keys;

    public uint[] SystemKeys;
    public uint[] SceneKeys;
    public uint[] MaterialKeys;
    public uint[] SubViewKeys;
    public ShaderNodePass[] Passes;
}
