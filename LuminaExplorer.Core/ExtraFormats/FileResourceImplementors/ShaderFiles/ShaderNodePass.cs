namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderNodePass {
    public uint Id;
    public uint VertexShader;
    public uint PixelShader;

    /// <summary>Hull shader index; present since version 0x0D01, otherwise <see cref="uint.MaxValue"/>.</summary>
    public uint HullShader;

    /// <summary>Domain shader index; present since version 0x0D01, otherwise <see cref="uint.MaxValue"/>.</summary>
    public uint DomainShader;

    /// <summary>Geometry shader index; present since version 0x0D01, otherwise <see cref="uint.MaxValue"/>.</summary>
    public uint GeometryShader;
}
