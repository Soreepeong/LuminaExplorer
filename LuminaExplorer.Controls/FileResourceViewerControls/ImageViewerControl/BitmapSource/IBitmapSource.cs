using System;
using System.Drawing;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;

public interface IBitmapSource : IDisposable, IAsyncDisposable {
    public event Action? ImageOrMipmapChanged;
    
    /// <summary>
    /// Number of elements in the array.
    /// 
    /// See <see cref="LuminaExplorer.Core.Util.TexToDds.DdsHeaderDxt10.ArraySize"/>
    /// </summary>
    public int ImageCount { get; }

    /// <summary>
    /// Number of mipmap levels.
    /// </summary>
    public int NumMipmaps { get; }

    /// <summary>
    /// Width of the first mipmap.
    /// </summary>
    public int BaseWidth { get; }

    /// <summary>
    /// Height of the first mipmap.
    /// </summary>
    public int BaseHeight { get; }

    /// <summary>
    /// Depth of the first mipmap.
    /// </summary>
    public int BaseDepth { get; }

    /// <summary>
    /// Spacing between slices in layout.
    /// </summary>
    public Size SliceSpacing { get; set; }

    /// <summary>
    /// Layout of the current mipmap.
    /// </summary>
    public IGridLayout Layout { get; }

    /// <summary>
    /// Width of the current mipmap.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the current mipmap.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Depth of the current mipmap.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Index of image being currently used.
    /// </summary>
    public int ImageIndex { get; set; }

    /// <summary>
    /// Mipmap being currently used. 
    /// </summary>
    public int Mipmap { get; set; }

    public void UpdateSelection(int imageIndex, int mipmap);

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int slice);

    public bool HasWicBitmapSource(int slice);

    public Task<Bitmap> GetGdipBitmapAsync(int slice);

    public bool HasGdipBitmap(int slice);
}