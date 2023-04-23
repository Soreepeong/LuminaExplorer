using System;

namespace LuminaExplorer.Core.Util.TexToDds;

[Flags]
public enum DdxHeaderDxt10MiscFlags {
    /// <summary>Indicates a 2D texture is a cube-map texture.</summary>
    TextureCube = 4,
}