using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public sealed class EmptyGridLayout : IGridLayout {
    public static readonly EmptyGridLayout Instance = new();

    private EmptyGridLayout() { }

    public int Count => 0;

    public Size GridSize => Size.Empty;

    public Rectangle RectOf(int cellIndex) => Rectangle.Empty;

    public GridLayoutCell this[int cellIndex] =>
        throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex, null);

    public IEnumerator<GridLayoutCell> GetEnumerator() => Enumerable.Empty<GridLayoutCell>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
