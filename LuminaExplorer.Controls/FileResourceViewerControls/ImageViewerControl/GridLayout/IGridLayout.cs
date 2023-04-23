using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public interface IGridLayout {
    private const float LayoutTableMaxRatio = 2.5f;

    public Size GridSize { get; }

    public Rectangle RectOf(int cellIndex);

    public RectangleF ScaleOf(int cellIndex) {
        var k = RectOf(cellIndex);
        return new(
            1f * k.X / GridSize.Width,
            1f * k.Y / GridSize.Height,
            1f * k.Width / GridSize.Width,
            1f * k.Height / GridSize.Height);
    }

    public RectangleF RectOf(int cellIndex, RectangleF actualGridRect) {
        var scaledRect = ScaleOf(cellIndex);
        return new(
            actualGridRect.X + scaledRect.Left * actualGridRect.Width,
            actualGridRect.Y + scaledRect.Top * actualGridRect.Height,
            scaledRect.Width * actualGridRect.Width,
            scaledRect.Height * actualGridRect.Height);
    }
    
    public static IGridLayout CreateGridLayout(int w, int h, int d, bool isCube, Size sliceSpacing) {
        if (w == 0 || h == 0 || d == 0)
            return EmptyGridLayout.Instance;

        if (d == 6 && isCube)
            return new CubeGridLayout(w, h, 0, 0);
        return new AutoGridLayout(w, h, sliceSpacing.Width, sliceSpacing.Height, d, LayoutTableMaxRatio);
    }
}
