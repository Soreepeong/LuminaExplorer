using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public sealed class CubeGridLayout : IGridLayout {
    private readonly GridLayoutCell[] _cells;

    public CubeGridLayout(
        int imageIndex,
        int mipmap,
        int cellWidth,
        int cellHeight,
        int horizontalSpacing,
        int verticalSpacing) {
        GridSize = new(cellWidth * 4 + horizontalSpacing * 3, cellHeight * 3 + verticalSpacing * 2);
        Spacing = new(horizontalSpacing, verticalSpacing);
        _cells = Enumerable.Range(0, 6)
            .Select(x => new GridLayoutCell(x, imageIndex, mipmap, x, cellWidth, cellHeight))
            .ToArray();
    }

    public int Count => 6;
    public Size GridSize { get; }
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
        var cell = _cells[cellIndex];
        return new(
            (cell.Width + Spacing.Width) * x,
            (cell.Height + Spacing.Height) * y,
            cell.Width,
            cell.Height);
    }

    public GridLayoutCell this[int cellIndex] => _cells[cellIndex];

    public IEnumerator<GridLayoutCell> GetEnumerator() => ((IEnumerable<GridLayoutCell>) _cells).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _cells.GetEnumerator();
}
