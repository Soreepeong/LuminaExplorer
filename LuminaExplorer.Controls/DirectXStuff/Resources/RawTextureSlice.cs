using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

/// <summary>
/// A single texture plane in a format that can be uploaded to the GPU as-is.
/// </summary>
/// <param name="Format">DXGI format of <paramref name="Data"/>.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="RowPitch">Number of bytes per row (of blocks, for block-compressed formats).</param>
/// <param name="Data">Texture data.</param>
/// <param name="ReplicateRedChannel">Whether the red channel should be shown as gray.</param>
public readonly record struct RawTextureSlice(
    DXGI_FORMAT Format,
    int Width,
    int Height,
    int RowPitch,
    byte[] Data,
    bool ReplicateRedChannel);
