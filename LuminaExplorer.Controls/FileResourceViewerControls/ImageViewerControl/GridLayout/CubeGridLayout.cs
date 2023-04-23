using System;
using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public sealed class CubeGridLayout : IGridLayout {
    public CubeGridLayout(
        int cellWidth,
        int cellHeight,
        int horizontalSpacing,
        int verticalSpacing) {
        CellSize = new(cellWidth, cellHeight);
        GridSize = new(cellWidth * 4 + horizontalSpacing * 3, cellHeight * 3 + verticalSpacing * 2);
        Spacing = new(horizontalSpacing, verticalSpacing);
    }

    public Size GridSize { get; }
    private Size CellSize { get; }
    private Size Spacing { get; }

    // Index ref
    // https://learn.microsoft.com/en-us/windows/win32/direct3ddds/dds-file-layout-for-cubic-environment-maps
    //
    // Unwrap ref
    // https://learnopengl.com/Advanced-OpenGL/Cubemaps
    public Rectangle RectOf(int cellIndex) {
        var (x, y) = cellIndex switch {
            // positive x (left)
            0 => (2, 1),
            // negative x (right)
            1 => (0, 1),
            // positive y (up)
            2 => (1, 0),
            // negative y (down)
            3 => (1, 2),
            // positive z (forward)
            4 => (1, 1),
            // negative z (back)
            5 => (3, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex, null),
        };
        return new(
            (CellSize.Width + Spacing.Width) * x,
            (CellSize.Height + Spacing.Height) * y,
            CellSize.Width,
            CellSize.Height);
    }
}
