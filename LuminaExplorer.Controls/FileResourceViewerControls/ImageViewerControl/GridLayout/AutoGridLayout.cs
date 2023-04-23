using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public sealed class AutoGridLayout : IGridLayout {
    public AutoGridLayout(
        int cellWidth,
        int cellHeight,
        int horizontalSpacing,
        int verticalSpacing,
        int items,
        float suggestedScaleBoundary) {
        if (items <= 0)
            return;

        CellSize = new(cellWidth, cellHeight);

        var flipped = cellWidth < cellHeight;
        if (flipped)
            (cellWidth, cellHeight) = (cellHeight, cellWidth);

        // width > height at this point
        // pick cells and rows that are:
        // 1. Find (cols, rows) candidates that satisfy 1 / N <= cellWidth * cols / cellHeight / rows <= N
        var candidates = new List<(int Cols, int Rows)>();
        for (var cols = 1; cols <= items; cols++) {
            var rows = (items + cols - 1) / cols;
            var m = 1f *
                    (cellWidth * cols + horizontalSpacing * (cols - 1)) /
                    (cellHeight * rows + verticalSpacing * (rows - 1));
            if (1 / suggestedScaleBoundary <= m && m <= suggestedScaleBoundary)
                candidates.Add((cols, rows));
        }

        // 1a. If none exists, find the (cols, rows) that minimizes the difference of (cellWidth * cols) and (cellHeight * rows).
        if (!candidates.Any()) {
            foreach (var x in new[] {cellWidth, cellHeight}) {
                foreach (var y in new[] {cellWidth, cellHeight}) {
                    var n = (int) Math.Max(1, Math.Sqrt(items * cellWidth * cellHeight) / x / y);
                    var m = (items + n - 1) / n;
                    candidates.Add((Cols: n, Rows: m));
                    candidates.Add((Cols: m, Rows: n));
                }
            }
        }

        // 2. Among the candidates, find the one with the least number of remainder cells, and choose the squarest one.
        var minRemainder = candidates.Min(x => items % x.Rows);
        var squarest = candidates
            .Where(x => items % x.Rows == minRemainder)
            .OrderBy(x =>
                x.Rows * cellHeight + (x.Rows - 1) * verticalSpacing -
                (x.Cols * cellWidth + (x.Cols - 1) * horizontalSpacing))
            .MinBy(x => Math.Abs(x.Cols * cellWidth - x.Rows * cellHeight));

        (Columns, Rows) = flipped ? (squarest.Rows, squarest.Cols) : (squarest.Cols, squarest.Rows);

        GridSize = new(
            CellSize.Width * Columns + horizontalSpacing * (Columns - 1),
            CellSize.Height * Rows + verticalSpacing * (Rows - 1));
        Spacing = new(horizontalSpacing, verticalSpacing);
    }

    public Size GridSize { get; }
    private Size CellSize { get; }
    private Size Spacing { get; }
    private int Columns { get; }
    private int Rows { get; }

    public Rectangle RectOf(int cellIndex) {
        var row = Math.DivRem(cellIndex, Columns, out var col);
        return new(
            col * (CellSize.Width + Spacing.Width),
            row * (CellSize.Height + Spacing.Height),
            CellSize.Width,
            CellSize.Height);
    }
}
