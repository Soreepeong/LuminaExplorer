using System.Drawing;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.Util;

public static class ColorExtensions {
    public static Color MultiplyOpacity(this Color color, float opacityScaled) =>
        Color.FromArgb((byte) (color.A * opacityScaled), color.R, color.G, color.B);

    public static DXGI_RGBA ToDxgiColor(this Color color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);
}
