using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;

public class GridLayoutCell {
    public readonly int CellIndex;
    public readonly int ImageIndex;
    public readonly int Mipmap;
    public readonly int Slice;
    public readonly int Width;
    public readonly int Height;

    public GridLayoutCell(int cellIndex, int imageIndex, int mipmap, int slice, int width, int height)
    {
        this.CellIndex = cellIndex;
        this.ImageIndex = imageIndex;
        this.Mipmap = mipmap;
        this.Slice = slice;
        this.Width = width;
        this.Height = height;
    }

    public Size Size => new(this.Width, this.Height);
}
