namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public enum ShaderType : ushort {
    Vertex = 0x0000,
    Pixel = 0x0100,
    Geometry = 0x0200,
    HullShader = 0x0400,
    DomainShader = 0x0500, 
}