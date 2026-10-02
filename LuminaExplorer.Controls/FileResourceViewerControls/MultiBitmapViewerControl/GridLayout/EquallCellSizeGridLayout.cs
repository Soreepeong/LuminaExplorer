using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;

public sealed class EquallCellSizeGridLayout : IGridLayout {
    private readonly GridLayoutCell[] _cells;

    public EquallCellSizeGridLayout(
        int horizontalSpacing,
        int verticalSpacing,
        IEnumerable<GridLayoutCell> cells,
        float suggestedScaleBoundary)
    {
        this._cells = cells.ToArray();
        if (!this._cells.Any())
            return;

        var cellWidth = this._cells[0].Width;
        var cellHeight = this._cells[0].Height;

        this.CellSize = new(cellWidth, cellHeight);

        var flipped = cellWidth < cellHeight;
        if (flipped)
            (cellWidth, cellHeight) = (cellHeight, cellWidth);

        // width > height at this point
        // pick cells and rows that are:
        // 1. Find (cols, rows) candidates that satisfy 1 / N <= cellWidth * cols / cellHeight / rows <= N
        var candidates = new List<(int Cols, int Rows)>();
        for (var cols = 1; cols <= this._cells.Length; cols++) {
            var rows = (this._cells.Length + cols - 1) / cols;
            var m = 1f *
                (cellWidth * cols + horizontalSpacing * (cols - 1)) /
                (cellHeight * rows + verticalSpacing * (rows - 1));
            if (1 / suggestedScaleBoundary <= m && m <= suggestedScaleBoundary)
                candidates.Add((cols, rows));
        }

        // 1a. If none exists, find the (cols, rows) that minimizes the difference of (cellWidth * cols) and (cellHeight * rows).
        if (!candidates.Any()) {
            foreach (var x in new[] { cellWidth, cellHeight }) {
                foreach (var y in new[] { cellWidth, cellHeight }) {
                    var n = (int) Math.Max(1, Math.Sqrt(this._cells.Length * cellWidth * cellHeight) / x / y);
                    var m = (this._cells.Length + n - 1) / n;
                    candidates.Add((Cols: n, Rows: m));
                    candidates.Add((Cols: m, Rows: n));
                }
            }
        }

        // 2. Among the candidates, find the one with the least number of remainder cells, and choose the squarest one.
        var minRemainder = candidates.Min(x => this._cells.Length % x.Rows);
        var squarest = candidates
            .Where(x => this._cells.Length % x.Rows == minRemainder)
            .OrderBy(
                x =>
                    x.Rows * cellHeight + (x.Rows - 1) * verticalSpacing -
                    (x.Cols * cellWidth + (x.Cols - 1) * horizontalSpacing))
            .MinBy(x => Math.Abs(x.Cols * cellWidth - x.Rows * cellHeight));

        (this.Columns, this.Rows) = flipped ? (squarest.Rows, squarest.Cols) : (squarest.Cols, squarest.Rows);

        this.GridSize = new(
            this.CellSize.Width * this.Columns + horizontalSpacing * (this.Columns - 1),
            this.CellSize.Height * this.Rows + verticalSpacing * (this.Rows - 1));
        this.Spacing = new(horizontalSpacing, verticalSpacing);
    }

    public int Count => this._cells.Length;
    public Size GridSize { get; }
    private Size CellSize { get; }
    private Size Spacing { get; }
    private int Columns { get; }
    private int Rows { get; }

    public Rectangle RectOf(int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= this.Count)
            throw new ArgumentOutOfRangeException(nameof(cellIndex), cellIndex, null);

        var row = Math.DivRem(cellIndex, this.Columns, out var col);
        return new(
            col * (this.CellSize.Width + this.Spacing.Width),
            row * (this.CellSize.Height + this.Spacing.Height),
            this.CellSize.Width,
            this.CellSize.Height);
    }

    public GridLayoutCell this[int cellIndex] => this._cells[cellIndex];

    public IEnumerator<GridLayoutCell> GetEnumerator() => ((IEnumerable<GridLayoutCell>) this._cells).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this._cells.GetEnumerator();
}
