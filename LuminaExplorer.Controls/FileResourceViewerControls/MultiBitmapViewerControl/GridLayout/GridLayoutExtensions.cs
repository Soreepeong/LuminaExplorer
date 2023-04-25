using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;

public static class GridLayoutExtensions {
    public static RectangleF ScaleOf(this IGridLayout layout, int cellIndex) {
        var k = layout.RectOf(cellIndex);
        var gridSize = layout.GridSize;
        return new(
            1f * k.X / gridSize.Width,
            1f * k.Y / gridSize.Height,
            1f * k.Width / gridSize.Width,
            1f * k.Height / gridSize.Height);
    }

    public static RectangleF RectOf(this IGridLayout layout, int cellIndex, RectangleF actualGridRect) {
        var scaledRect = layout.ScaleOf(cellIndex);
        return new(
            actualGridRect.X + scaledRect.Left * actualGridRect.Width,
            actualGridRect.Y + scaledRect.Top * actualGridRect.Height,
            scaledRect.Width * actualGridRect.Width,
            scaledRect.Height * actualGridRect.Height);
    }

    public static RectangleF RectOf(this IGridLayout layout, GridLayoutCell cell) => layout.RectOf(cell.CellIndex);

    public static RectangleF ScaleOf(this IGridLayout layout, GridLayoutCell cell) => layout.ScaleOf(cell.CellIndex);

    public static RectangleF RectOf(this IGridLayout layout, GridLayoutCell cell, RectangleF actualGridRect) =>
        layout.RectOf(cell.CellIndex, actualGridRect);
}
