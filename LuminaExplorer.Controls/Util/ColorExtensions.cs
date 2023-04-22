namespace LuminaExplorer.Controls.Util; 

public static class ColorExtensions {
    public static Color MultiplyOpacity(this Color color, float opacityScaled) =>
        Color.FromArgb((byte) (color.A * opacityScaled), color.R, color.G, color.B);
}
