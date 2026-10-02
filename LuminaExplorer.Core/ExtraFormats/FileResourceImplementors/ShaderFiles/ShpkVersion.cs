namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public enum ShpkVersion : uint {
    V0B01 = 0x0b01,

    /// <summary>Adds hull/domain/geometry shaders, per-shader unknown value, per-node unknown keys.</summary>
    V0D01 = 0x0d01,

    /// <summary>Adds node alias clusters.</summary>
    V0E01 = 0x0e01,
}
