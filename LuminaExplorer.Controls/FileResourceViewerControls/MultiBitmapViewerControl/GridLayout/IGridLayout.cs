using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;

public interface IGridLayout : IEnumerable<GridLayoutCell> {
    private const float LayoutTableMaxRatio = 2.5f;
    
    public int Count { get; }

    public Size GridSize { get; }

    public Rectangle RectOf(int cellIndex);

    public GridLayoutCell this[int cellIndex] { get; }

    public static IGridLayout CreateGridLayoutForDepthView(int imageIndex, int mipmap, int w, int h, int d, bool isCube, Size sliceSpacing) {
        if (w == 0 || h == 0 || d == 0)
            return EmptyGridLayout.Instance;

        if (d == 6 && isCube)
            return new CubeGridLayout(imageIndex, mipmap, w, h, 0, 0);
        return new EquallCellSizeGridLayout(
            sliceSpacing.Width,
            sliceSpacing.Height,
            Enumerable.Range(0, d).Select(x => new GridLayoutCell(x, imageIndex, mipmap, x, w, h)),
            LayoutTableMaxRatio);
    }
}
