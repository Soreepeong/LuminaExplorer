using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using DdsFile = LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.DdsFile;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public sealed class DdsBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private DdsFile _ddsFile;
    private ResultDisposingTask<ComPtr<IWICBitmapSource>>?[ /* Image */][ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Image */][ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _imageIndex;
    private int _mipmap;
    private bool _disposed;

    public DdsBitmapSource(DdsFile ddsFile, int imageIndex = 0, int mipmap = 0, Size sliceSpacing = new())
    {
        this._ddsFile = ddsFile;

        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= ddsFile.NumMipmaps)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        this._wicBitmaps = new ResultDisposingTask<ComPtr<IWICBitmapSource>>[this.ImageCount][][];
        this._bitmaps = new ResultDisposingTask<Bitmap>[this.ImageCount][][];
        for (var image = 0; image < this.ImageCount; image++) {
            var imageWicBitmaps = this._wicBitmaps[image] =
                new ResultDisposingTask<ComPtr<IWICBitmapSource>>?[ddsFile.NumMipmaps][];
            var imageBitmaps = this._bitmaps[image] = new ResultDisposingTask<Bitmap>?[ddsFile.NumMipmaps][];
            for (var mip = 0; mip < ddsFile.NumMipmaps; mip++) {
                imageWicBitmaps[mip] = new ResultDisposingTask<ComPtr<IWICBitmapSource>>?[ddsFile.DepthOrNumFaces(mip)];
                imageBitmaps[mip] = new ResultDisposingTask<Bitmap>?[ddsFile.DepthOrNumFaces(mip)];
            }
        }

        this.IsCubeMap = this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.Cubemap);

        this._sliceSpacing = sliceSpacing;

        this.Layout = null!;
        this._imageIndex = -1;
        this._mipmap = -1;
        this.UpdateSelection(imageIndex, mipmap);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;
        this._disposed = true;
        this._ddsFile = null!;
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
        this._ddsFile = null!;
        this._cancellationTokenSource.Cancel();

        await Task.WhenAll(
            SafeDispose.EnumerableAsync(ref this._wicBitmaps!),
            SafeDispose.EnumerableAsync(ref this._bitmaps!));

        this._cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public string FileName => this._ddsFile.Name;

    public int ImageCount => this._ddsFile.NumImages;

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
        get => this._imageIndex;
        set {
            if (this._imageIndex == value)
                return;
            if (value < 0 || value >= this.ImageCount)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            this._imageIndex = value;
            this.Relayout();
            this.LayoutChanged?.Invoke();
        }
    }

    public int Mipmap {
        get => this._mipmap;
        set {
            if (this._mipmap == value)
                return;
            if (value < 0 || value >= this.NumberOfMipmaps(this.ImageIndex))
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            this._mipmap = value;
            this.Relayout();
            this.LayoutChanged?.Invoke();
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap)
    {
        if (this._imageIndex == imageIndex && this._mipmap == mipmap)
            return;

        this._imageIndex = imageIndex;
        this._mipmap = mipmap;
        this.Relayout();
        this.LayoutChanged?.Invoke();
    }

    public Task<ComPtr<IWICBitmapSource>> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (this._wicBitmaps[imageIndex][mipmap][slice] ??= new(
            Task.Run(
                () => this._ddsFile.ToWicBitmapSource(imageIndex, mipmap, slice),
                this._cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice)
    {
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._wicBitmaps[imageIndex][mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (this._bitmaps[imageIndex][mipmap][slice] ??= new(
            this.GetWicBitmapSourceAsync(imageIndex, mipmap, slice)
                .ContinueWith(
                    r => {
                        if (this._wicBitmaps[imageIndex][mipmap][slice] is
                            { Task.IsCompletedSuccessfully: true } wicBitmapTask) {
                            if (wicBitmapTask.Result.TryToGdipBitmap(out var b, out _))
                                return b;
                        }

                        var bitmap = new Bitmap(
                            this._ddsFile.Width(mipmap),
                            this._ddsFile.Height(mipmap),
                            PixelFormat.Format32bppArgb);
                        try {
                            var lb = bitmap.LockBits(
                                new(Point.Empty, bitmap.Size),
                                ImageLockMode.WriteOnly,
                                PixelFormat.Format32bppArgb);
                            unsafe {
                                this._ddsFile.PixFmt.ToB8G8R8A8(
                                    new((void*) lb.Scan0, lb.Stride * lb.Height),
                                    lb.Stride,
                                    this._ddsFile.SliceOrFaceData(imageIndex, mipmap, slice),
                                    this._ddsFile.Pitch(mipmap),
                                    lb.Width,
                                    lb.Height);
                            }

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
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._bitmaps[imageIndex][mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex)
    {
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._ddsFile.NumMipmaps;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._ddsFile.Width(mipmap);
    }

    public int HeightOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._ddsFile.Height(mipmap);
    }

    public int NumSlicesOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex < 0 || imageIndex >= this.ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= this.NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return this._ddsFile.DepthOrNumFaces(mipmap);
    }

    public void WriteTexFile(Stream stream)
    {
        throw new NotImplementedException();
    }

    public void WriteDdsFile(Stream stream)
    {
        using var ms = this._ddsFile.CreateStream();
        ms.CopyTo(stream);
    }

    public void DescribeImage(StringBuilder sb)
    {
        if (this._ddsFile.Header.PixelFormat.Flags != DdsPixelFormatFlags.FourCc)
            sb.Append(this._ddsFile.Header.PixelFormat.Flags & ~DdsPixelFormatFlags.FourCc).Append("; ");

        sb.Append($"{this._ddsFile.DataOffset + this._ddsFile.Body.Length:##,###} Bytes");
        if (this._ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.MipmapCount))
            sb.Append("; ").Append(this._ddsFile.NumMipmaps).Append(" Mipmaps");
        sb.AppendLine();

        if (this._ddsFile.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc)) {
            if (this._ddsFile.Header.PixelFormat.Flags != DdsPixelFormatFlags.FourCc)
                sb.Append(this._ddsFile.Header.PixelFormat.Flags & ~DdsPixelFormatFlags.FourCc).Append("; ");
            var uival = (uint) this._ddsFile.Header.PixelFormat.FourCc;
            var c1 = (char) Math.Clamp((uival >> 0) & 0xFF, 0x20, 0x7F);
            var c2 = (char) Math.Clamp((uival >> 8) & 0xFF, 0x20, 0x7F);
            var c3 = (char) Math.Clamp((uival >> 16) & 0xFF, 0x20, 0x7F);
            var c4 = (char) Math.Clamp((uival >> 24) & 0xFF, 0x20, 0x7F);
            sb.AppendLine($"FourCC=0x{uival:X08}({c1}{c2}{c3}{c4})");
        } else {
            var inferredFourCc = this._ddsFile.PixFmt.FourCc;
            if (inferredFourCc != this._ddsFile.Header.PixelFormat.FourCc && inferredFourCc != DdsFourCc.Unknown) {
                var uival = (uint) inferredFourCc;
                var c1 = (char) Math.Clamp((uival >> 0) & 0xFF, 0x20, 0x7F);
                var c2 = (char) Math.Clamp((uival >> 8) & 0xFF, 0x20, 0x7F);
                var c3 = (char) Math.Clamp((uival >> 16) & 0xFF, 0x20, 0x7F);
                var c4 = (char) Math.Clamp((uival >> 24) & 0xFF, 0x20, 0x7F);
                sb.AppendLine($"FourCC(inferred)=0x{uival:X08}({c1}{c2}{c3}{c4})");
            }
        }

        if (this._ddsFile.UseDxt10Header)
            sb.AppendLine(
                $"DxgiFormat={this._ddsFile.Dxt10Header.DxgiFormat} ({(int) this._ddsFile.Dxt10Header.DxgiFormat})");
        else {
            var inferredDxgiFormat = this._ddsFile.PixFmt.DxgiFormat;
            if (inferredDxgiFormat != DXGI_FORMAT.DXGI_FORMAT_UNKNOWN &&
                (!this._ddsFile.UseDxt10Header || inferredDxgiFormat != this._ddsFile.Dxt10Header.DxgiFormat)) {
                sb.AppendLine($"DxgiFormat(inferred)={inferredDxgiFormat} ({(int) inferredDxgiFormat})");
            }
        }

        var inferredWicPixelFormat = this._ddsFile.PixFmt.WicFormat;
        if (inferredWicPixelFormat != GUID.GUID_WICPixelFormatUndefined) {
            unsafe {
                using var info = new ComPtr<IWICComponentInfo>();
                ImagingExtensions.WicFactory.Get()->CreateComponentInfo(
                    &inferredWicPixelFormat,
                    info.GetAddressOf()).Ensure();

                uint cch;
                info.Get()->GetFriendlyName(0, null, &cch).Ensure();
                var buf = stackalloc char[(int) cch + 1];
                info.Get()->GetFriendlyName(cch + 1, buf, &cch).Ensure();
                sb.AppendLine($"WicPixelFormat(inferred)={new(buf, 0, (int) cch)}");
            }
        }

        var useW = this._ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Width);
        var useH = this._ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Height);
        var useD = this._ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Depth);

        var isStandardCube = useW && useH && !useD && this._ddsFile.NumFaces == 6;
        var isStandard1D = useW && !useH && !useD && !this._ddsFile.IsCubeMap;
        var isStandard2D = useW && useH && !useD && !this._ddsFile.IsCubeMap;
        var isStandard3D = useW && useH && useD && !this._ddsFile.IsCubeMap;
        var isNonstandard = !isStandardCube && !isStandard1D && !isStandard2D && !isStandard3D;
        if (isStandard1D)
            sb.Append("1D: ").Append(this._ddsFile.Header.Width);
        else if (isStandard2D)
            sb.Append("2D: ").Append(this._ddsFile.Header.Width)
                .Append(" x ").Append(this._ddsFile.Header.Height);
        else if (isStandard3D)
            sb.Append("3D: ").Append(this._ddsFile.Header.Width)
                .Append(" x ").Append(this._ddsFile.Header.Height)
                .Append(" x ").Append(this._ddsFile.Header.Depth);
        else if (isStandardCube)
            sb.Append("Cube: ").Append(this._ddsFile.Header.Width)
                .Append(" x ").Append(this._ddsFile.Header.Height);
        else {
            sb.Append("Nonstandard;");
            if (useW) sb.Append(" W=").Append(this._ddsFile.Header.Width);
            if (useH) sb.Append(" H=").Append(this._ddsFile.Header.Height);
            if (useD) sb.Append(" D=").Append(this._ddsFile.Header.Depth);
        }

        if (this._ddsFile.UseDxt10Header && this._ddsFile.Dxt10Header.ArraySize != 1)
            sb.Append($" [{this._ddsFile.Dxt10Header.ArraySize}]");

        if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.Volume))
            sb.Append("; Volume");

        sb.AppendLine();

        if (isNonstandard && this.IsCubeMap) {
            sb.Append("Cube Faces: ");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveX))
                sb.Append(" +X");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeX))
                sb.Append(" -X");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveY))
                sb.Append(" +Y");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeY))
                sb.Append(" -Y");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveZ))
                sb.Append(" +Z");
            if (this._ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeZ))
                sb.Append(" -Z");
            sb.AppendLine();
        }

        if (this._mipmap > 0) {
            sb.AppendLine().Append("Mipmap #").Append(this._mipmap + 1).Append(": ");
            if (isStandard1D)
                sb.Append(this.WidthOfMipmap(0, this._mipmap));
            else if (isStandard2D)
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap));
            else if (isStandard3D)
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.NumSlicesOfMipmap(0, this._mipmap));
            else if (isStandardCube)
                sb.Append(this.WidthOfMipmap(0, this._mipmap))
                    .Append(" x ").Append(this.HeightOfMipmap(0, this._mipmap));
            else {
                if (useW) sb.Append(" W=").Append(this.WidthOfMipmap(0, this._mipmap));
                if (useH) sb.Append(" H=").Append(this.HeightOfMipmap(0, this._mipmap));
                if (useD) sb.Append(" D=").Append(this._ddsFile.DepthOrNumFaces(this._mipmap));
            }

            sb.AppendLine();
        }
    }

    private void Relayout()
    {
        var width = this.WidthOfMipmap(this._imageIndex, this._mipmap);
        var height = this.HeightOfMipmap(this._imageIndex, this._mipmap);
        var slices = this.NumSlicesOfMipmap(this._imageIndex, this._mipmap);

        this.Layout = IGridLayout.CreateGridLayoutForDepthView(
            this._imageIndex,
            this._mipmap,
            width,
            height,
            slices,
            this.IsCubeMap,
            this._sliceSpacing);
    }
}
