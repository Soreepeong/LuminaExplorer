using Lumina.Data.Files;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private IGridLayout CreateGridLayout(int mipmapIndex) {
        if (FileResourceTyped is not { } tf || mipmapIndex >= tf.TextureBuffer.MipmapAllocations.Length)
            return new AutoGridLayout(0, 0, 0, 0, 0, 0);
        var w = tf.TextureBuffer.WidthOfMipmap(mipmapIndex);
        var h = tf.TextureBuffer.HeightOfMipmap(mipmapIndex);
        var d = tf.TextureBuffer.DepthOfMipmap(mipmapIndex);
        if (w == 0 || h == 0 || d == 0)
            return EmptyGridLayout.Instance;

        if (d == 6 && tf.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            return new CubeGridLayout(w, h, 0, 0);
        return new AutoGridLayout(w, h, SliceSpacing.Width, SliceSpacing.Height, d, LayoutTableMaxRatio);
    }

    private interface IGridLayout {
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

        public Rectangle RectOf(int cellIndex, Rectangle actualGridRect) {
            var scaledRect = ScaleOf(cellIndex);
            return new(
                (int)(actualGridRect.X + scaledRect.Left * actualGridRect.Width),
                (int)(actualGridRect.Y + scaledRect.Top * actualGridRect.Height),
                (int)(scaledRect.Width * actualGridRect.Width),
                (int)(scaledRect.Height * actualGridRect.Height));
        }
    }

    private sealed class EmptyGridLayout : IGridLayout {
        public static readonly EmptyGridLayout Instance = new();

        private EmptyGridLayout() { }

        public Size GridSize => Size.Empty;
        public Rectangle RectOf(int cellIndex) => Rectangle.Empty;
    }

    private sealed class AutoGridLayout : IGridLayout {
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

    private sealed class CubeGridLayout : IGridLayout {
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
}
