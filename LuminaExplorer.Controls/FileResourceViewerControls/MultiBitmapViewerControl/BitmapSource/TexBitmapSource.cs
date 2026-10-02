using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public sealed class TexBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private TexFile _texFile;
    private ResultDisposingTask<ComPtr<IWICBitmapSource>>?[ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _mipmap;
    private bool _disposed;

    public TexBitmapSource(TexFile texFile, int mipmap = 0, Size sliceSpacing = new())
    {
        if (mipmap < 0 || mipmap >= texFile.Header.MipCount)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        this._texFile = texFile;
        this._wicBitmaps = new ResultDisposingTask<ComPtr<IWICBitmapSource>>[this._texFile.Header.MipCount][];
        this._bitmaps = new ResultDisposingTask<Bitmap>[this._texFile.Header.MipCount][];
        for (var i = 0; i < this._texFile.Header.MipCount; i++) {
            var slices = texFile.TextureBuffer.DepthOfMipmap(i);
            this._wicBitmaps[i] = new ResultDisposingTask<ComPtr<IWICBitmapSource>>?[slices];
            this._bitmaps[i] = new ResultDisposingTask<Bitmap>?[slices];
        }

        this.IsCubeMap = this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube);

        this._sliceSpacing = sliceSpacing;

        this.Layout = null!;
        this._mipmap = -1;
        this.UpdateSelection(0, mipmap);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;
        this._disposed = true;
        this._texFile = null!;
        this._cancellationTokenSource.Cancel();

        SafeDispose.Enumerable(ref this._wicBitmaps!);
        SafeDispose.Enumerable(ref this._bitmaps!);

        this._cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;
        this._disposed = true;
        this._texFile = null!;
        this._cancellationTokenSource.Cancel();

        await Task.WhenAll(
            SafeDispose.EnumerableAsync(ref this._wicBitmaps!),
            SafeDispose.EnumerableAsync(ref this._bitmaps!));

        this._cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public string FileName => Path.GetFileName(this._texFile.FilePath.Path);

    public int ImageCount => 1;

    public IGridLayout Layout { get; private set; }

    public bool IsCubeMap { get; }

    public Size SliceSpacing {
        get => this._sliceSpacing;
        set {
            if (this._sliceSpacing == value)
                return;

            this._sliceSpacing = value;
            this.Relayout();
        }
    }

    public int ImageIndex {
        get => 0;
        set {
            if (value != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }
    }

    public int Mipmap {
        get => this._mipmap;
        set {
            if (this._mipmap == value)
                return;
            if (value < 0 || value >= this._texFile.Header.MipCount)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            this._mipmap = value;
            this.Relayout();
            this.LayoutChanged?.Invoke();
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap)
    {
        this.ImageIndex = imageIndex;
        this.Mipmap = mipmap;
    }

    public Task<ComPtr<IWICBitmapSource>> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (this._wicBitmaps[mipmap][slice] ??= new(
            Task.Run(
                () => this._texFile.ToWicBitmapSource(mipmap, slice),
                this._cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._wicBitmaps[mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    public bool SupportsRawSlice(int imageIndex, int mipmap)
    {
        if (this._disposed || imageIndex != 0 || mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            return false;
        if (!ResourceUtils.TryGetDirectDxgiFormat(this._texFile.Header.Format, out var format, out _))
            return false;

        // D3D11 requires the dimensions of a block-compressed texture to be multiples of 4.
        return !format.IsBlockCompressed() ||
            (this.WidthOfMipmap(imageIndex, mipmap) % 4 == 0 && this.HeightOfMipmap(imageIndex, mipmap) % 4 == 0);
    }

    public RawTextureSlice GetRawSlice(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (!this.SupportsRawSlice(imageIndex, mipmap))
            throw new NotSupportedException();

        ResourceUtils.TryGetDirectDxgiFormat(this._texFile.Header.Format, out var format, out var replicateRedChannel);
        var texBuf = this._texFile.TextureBuffer.Filter(mipmap, slice);
        var rows = format.IsBlockCompressed() ? (texBuf.Height + 3) / 4 : texBuf.Height;
        return new(
            format,
            texBuf.Width,
            texBuf.Height,
            texBuf.RawData.Length / rows,
            texBuf.RawData,
            replicateRedChannel);
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (this._bitmaps[mipmap][slice] ??= new(
            Task.Run(
                () => {
                    // The WIC conversion handles more formats than Lumina's conversion to B8G8R8A8.
                    try {
                        var wicBitmap = this.GetWicBitmapSourceAsync(imageIndex, mipmap, slice)
                            .WaitAsync(this._cancellationTokenSource.Token)
                            .GetAwaiter()
                            .GetResult();
                        if (wicBitmap.TryToGdipBitmap(out var b, out _))
                            return b;
                    } catch (Exception e) when (e is not OperationCanceledException) {
                        // Fall back to Lumina's conversion.
                    }

                    var texBuf = this._texFile.TextureBuffer.Filter(mipmap, slice, TexFile.TextureFormat.B8G8R8A8);
                    var bitmap = new Bitmap(texBuf.Width, texBuf.Height, PixelFormat.Format32bppArgb);
                    try {
                        var lb = bitmap.LockBits(
                            new(Point.Empty, bitmap.Size),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);
                        Marshal.Copy(texBuf.RawData, 0, lb.Scan0, lb.Stride * lb.Height);
                        bitmap.UnlockBits(lb);

                        return bitmap;
                    } catch (Exception) {
                        bitmap.Dispose();
                        throw;
                    }
                },
                this._cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._bitmaps[mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._texFile.Header.MipCount;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._texFile.TextureBuffer.WidthOfMipmap(mipmap);
    }

    public int HeightOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._texFile.TextureBuffer.HeightOfMipmap(mipmap);
    }

    public int NumSlicesOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._texFile.TextureBuffer.DepthOfMipmap(mipmap);
    }

    public void WriteTexFile(Stream stream) => stream.Write(this._texFile.Data);

    public void WriteDdsFile(Stream stream)
    {
        using var ms = this._texFile.ToDdsFile().CreateStream();
        ms.CopyTo(stream);
    }

    public void DescribeImage(StringBuilder sb)
    {
        sb.Append(this._texFile.Header.Format).Append("; ")
            .Append($"{this._texFile.Data.Length:##,###} Bytes");
        if (this._texFile.Header.MipCount > 1)
            sb.Append("; ").Append(this._texFile.Header.MipCount).Append(" Mipmaps");
        sb.AppendLine();

        if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
            sb.Append("1D: ").Append(this._texFile.Header.Width);
        if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
            sb.Append("2D: ").Append(this._texFile.Header.Width)
                .Append(" x ").Append(this._texFile.Header.Height);
        if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
            sb.Append("3D: ").Append(this._texFile.Header.Width)
                .Append(" x ").Append(this._texFile.Header.Height)
                .Append(" x ").Append(this._texFile.Header.Depth);
        if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            sb.Append("Cube: ").Append(this._texFile.Header.Width)
                .Append(" x ").Append(this._texFile.Header.Height);
        if (this._mipmap > 0) {
            sb.AppendLine().Append("Mipmap #").Append(this._mipmap + 1).Append(": ");
            if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                sb.Append(this.WidthOfMipmap(0, this._mipmap));
            if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap));
            if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.NumSlicesOfMipmap(0, this._mipmap));
            if (this._texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap));
        }

        sb.AppendLine();
        foreach (var f in new[] {
                     TexFile.Attribute.DiscardPerFrame,
                     TexFile.Attribute.DiscardPerMap,
                     TexFile.Attribute.Managed,
                     TexFile.Attribute.UserManaged,
                     TexFile.Attribute.CpuRead,
                     TexFile.Attribute.LocationMain,
                     TexFile.Attribute.NoGpuRead,
                     TexFile.Attribute.AlignedSize,
                     TexFile.Attribute.EdgeCulling,
                     TexFile.Attribute.LocationOnion,
                     TexFile.Attribute.ReadWrite,
                     TexFile.Attribute.Immutable,
                     TexFile.Attribute.TextureRenderTarget,
                     TexFile.Attribute.TextureDepthStencil,
                     TexFile.Attribute.TextureSwizzle,
                     TexFile.Attribute.TextureNoTiled,
                     TexFile.Attribute.TextureNoSwizzle,
                 })
            if (this._texFile.Header.Type.HasFlag(f))
                sb.Append("+ ").AppendLine(f.ToString());
    }

    private void Relayout()
    {
        var width = this._texFile.TextureBuffer.WidthOfMipmap(this._mipmap);
        var height = this._texFile.TextureBuffer.HeightOfMipmap(this._mipmap);
        var depth = this._texFile.TextureBuffer.DepthOfMipmap(this._mipmap);

        this.Layout = IGridLayout.CreateGridLayoutForDepthView(
            0,
            this._mipmap,
            width,
            height,
            depth,
            this.IsCubeMap,
            this._sliceSpacing);
    }

    public static TexFile FromFile(FileInfo fileInfo)
    {
        var data = File.ReadAllBytes(fileInfo.FullName);

        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var file = (TexFile) Activator.CreateInstance(typeof(TexFile))!;
        var luminaFileInfo = new LuminaFileInfo {
            Type = FileType.Texture,
        };

        var pfp = new ParsedFilePath();
        typeof(ParsedFilePath).GetProperty("Path", bindingFlags)!.SetValue(pfp, fileInfo.FullName);

        typeof(FileResource).GetProperty("FileInfo", bindingFlags)!.SetValue(file, luminaFileInfo);
        typeof(FileResource).GetProperty("FilePath", bindingFlags)!.SetValue(file, pfp);
        typeof(FileResource).GetProperty("Data", bindingFlags)!.SetValue(file, data);
        typeof(FileResource).GetProperty("Reader", bindingFlags)!.SetValue(file, new LuminaBinaryReader(data));
        typeof(FileResource).GetMethod("LoadFile", bindingFlags)!.Invoke(file, null);
        return file;
    }
}
