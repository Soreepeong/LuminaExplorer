using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public class GridLayoutCell {
    public readonly int CellIndex;
    public readonly int ImageIndex;
    public readonly int Mipmap;
    public readonly int Slice;
    public readonly int Width;
    public readonly int Height;

    public GridLayoutCell(int cellIndex, int imageIndex, int mipmap, int slice, int width, int height) {
        CellIndex = cellIndex;
        ImageIndex = imageIndex;
        Mipmap = mipmap;
        Slice = slice;
        Width = width;
        Height = height;
    }

    public Size Size => new(Width, Height);
}
