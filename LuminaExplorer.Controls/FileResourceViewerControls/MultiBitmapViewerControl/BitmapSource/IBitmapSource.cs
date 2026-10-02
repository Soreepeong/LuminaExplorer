using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using TerraFX.Interop.Windows;

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

    public bool IsCubeMap { get; }

    public void UpdateSelection(int imageIndex, int mipmap);

    public Task<ComPtr<IWICBitmapSource>> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice);

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice);

    /// <summary>
    /// Checks whether <see cref="GetRawSlice"/> can provide the data of the given mipmap for uploading to the GPU
    /// without conversion.
    /// </summary>
    public bool SupportsRawSlice(int imageIndex, int mipmap) => false;

    /// <summary>
    /// Gets the data of a slice for uploading to the GPU without conversion.
    /// Only valid if <see cref="SupportsRawSlice"/> returns true.
    /// </summary>
    public RawTextureSlice GetRawSlice(int imageIndex, int mipmap, int slice) => throw new NotSupportedException();

    public Task<Bitmap> GetGdipBitmapAsync(int imageIndex, int mipmap, int slice);

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice);

    public int NumberOfMipmaps(int imageIndex);

    public int WidthOfMipmap(int imageIndex, int mipmap);

    public int HeightOfMipmap(int imageIndex, int mipmap);

    public int NumSlicesOfMipmap(int imageIndex, int mipmap);

    public void WriteTexFile(Stream stream);

    public void WriteDdsFile(Stream stream);

    public void DescribeImage(StringBuilder sb);
}
