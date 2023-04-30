namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderNode {
    public uint Id;
    public byte[] PassIndices;
    public uint[] SystemKeys;
    public uint[] SceneKeys;
    public uint[] MaterialKeys;
    public uint[] SubViewKeys;
    public ShaderNodePass[] Passes;
}