using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.Util.DdsStructs;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public interface IBitmapSource : IDisposable, IAsyncDisposable {
    public event Action? LayoutChanged;

    public string FileName { get; }

    /// <summary>
    /// Number of elements in the array.
    /// 
    /// See <see cref="DdsHeaderDxt10.ArraySize"/>
    /// </summary>
    public int ImageCount { get; }

    /// <summary>
    /// Spacing between slices in layout.
    /// </summary>
    public Size SliceSpacing { get; set; }

    /// <summary>
    /// Layout of the current mipmap.
    /// </summary>
    public IGridLayout Layout { get; }

    public void UpdateSelection(int imageIndex, int mipmap);

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice);

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice);

    public Task<Bitmap> GetGdipBitmapAsync(int imageIndex, int mipmap, int slice);

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice);

    public int NumberOfMipmaps(int imageIndex);

    public int WidthOfMipmap(int imageIndex, int mipmap);

    public int HeightOfMipmap(int imageIndex, int mipmap);

    public int DepthOfMipmap(int imageIndex, int mipmap);

    public void WriteTexFile(Stream stream);

    public void WriteDdsFile(Stream stream);

    public void DescribeImage(StringBuilder sb);
}
