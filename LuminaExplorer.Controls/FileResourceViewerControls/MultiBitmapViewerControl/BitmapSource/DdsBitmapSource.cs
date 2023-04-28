using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public sealed class DdsBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private DdsFile _ddsFile;
    private ResultDisposingTask<WicBitmapSource>?[ /* Image */][ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Image */][ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _imageIndex;
    private int _mipmap;
    private bool _disposed;

    public DdsBitmapSource(DdsFile ddsFile, int imageIndex = 0, int mipmap = 0, Size sliceSpacing = new()) {
        _ddsFile = ddsFile;

        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= ddsFile.NumMipmaps)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        _wicBitmaps = new ResultDisposingTask<WicBitmapSource>[ImageCount][][];
        _bitmaps = new ResultDisposingTask<Bitmap>[ImageCount][][];
        for (var image = 0; image < ImageCount; image++) {
            var imageWicBitmaps =
                _wicBitmaps[image] = new ResultDisposingTask<WicBitmapSource>?[ddsFile.NumMipmaps][];
            var imageBitmaps = _bitmaps[image] = new ResultDisposingTask<Bitmap>?[ddsFile.NumMipmaps][];
            for (var mip = 0; mip < ddsFile.NumMipmaps; mip++) {
                imageWicBitmaps[mip] = new ResultDisposingTask<WicBitmapSource>?[ddsFile.DepthOrNumFaces(mip)];
                imageBitmaps[mip] = new ResultDisposingTask<Bitmap>?[ddsFile.DepthOrNumFaces(mip)];
            }
        }

        IsCubeMap = _ddsFile.Header.Caps2.HasFlag(DdsCaps2.Cubemap);

        _sliceSpacing = sliceSpacing;

        Layout = null!;
        _imageIndex = -1;
        _mipmap = -1;
        UpdateSelection(imageIndex, mipmap);
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _ddsFile = null!;
        _cancellationTokenSource.Cancel();

        SafeDispose.Enumerable(ref _wicBitmaps!);
        SafeDispose.Enumerable(ref _bitmaps!);

        _cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;
        _disposed = true;
        _ddsFile = null!;
        _cancellationTokenSource.Cancel();

        await Task.WhenAll(
            SafeDispose.EnumerableAsync(ref _wicBitmaps!),
            SafeDispose.EnumerableAsync(ref _bitmaps!));

        _cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public string FileName => _ddsFile.Name;

    public int ImageCount => _ddsFile.NumMipmaps;

    public IGridLayout Layout { get; private set; }

    public bool IsCubeMap { get; }

    public Size SliceSpacing {
        get => _sliceSpacing;
        set {
            if (_sliceSpacing == value)
                return;

            _sliceSpacing = value;
            Relayout();
        }
    }

    public int ImageIndex {
        get => _imageIndex;
        set {
            if (_imageIndex == value)
                return;
            if (value < 0 || value >= ImageCount)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            _imageIndex = value;
            Relayout();
            LayoutChanged?.Invoke();
        }
    }

    public int Mipmap {
        get => _mipmap;
        set {
            if (_mipmap == value)
                return;
            if (value < 0 || value >= NumberOfMipmaps(ImageIndex))
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            _mipmap = value;
            Relayout();
            LayoutChanged?.Invoke();
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap) {
        if (_imageIndex == imageIndex && _mipmap == mipmap)
            return;

        _imageIndex = imageIndex;
        _mipmap = mipmap;
        Relayout();
        LayoutChanged?.Invoke();
    }

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_wicBitmaps[imageIndex][mipmap][slice] ??= new(Task.Run(
            () => _ddsFile.ToWicBitmapSource(imageIndex, mipmap, slice),
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _wicBitmaps[imageIndex][mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_bitmaps[imageIndex][mipmap][slice] ??= new(GetWicBitmapSourceAsync(imageIndex, mipmap, slice)
            .ContinueWith(r => {
                if (_wicBitmaps[imageIndex][mipmap][slice] is {Task.IsCompletedSuccessfully: true} wicBitmapTask) {
                    if (wicBitmapTask.Result.TryToGdipBitmap(out var b, out _))
                        return b;
                }

                var bitmap = new Bitmap(
                    _ddsFile.Width(mipmap),
                    _ddsFile.Height(mipmap), 
                    PixelFormat.Format32bppArgb);
                try {
                    var lb = bitmap.LockBits(
                        new(Point.Empty, bitmap.Size),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);
                    unsafe {
                        _ddsFile.PixFmt.ToB8G8R8A8(
                            new((void*) lb.Scan0, lb.Stride * lb.Height),
                            lb.Stride,
                            _ddsFile.SliceOrFaceData(imageIndex, mipmap, slice),
                            _ddsFile.Pitch(mipmap),
                            lb.Width,
                            lb.Height);
                    }
                    bitmap.UnlockBits(lb);

                    return bitmap;
                } catch (Exception) {
                    bitmap.Dispose();
                    throw;
                }
            }, _cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _bitmaps[imageIndex][mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.NumMipmaps;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.Width(mipmap);
    }

    public int HeightOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.Height(mipmap);
    }

    public int NumSlicesOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.DepthOrNumFaces(mipmap);
    }

    public void WriteTexFile(Stream stream) {
        throw new NotImplementedException();
    }

    public void WriteDdsFile(Stream stream) {
        using var ms = _ddsFile.CreateStream();
        ms.CopyTo(stream);
    }

    public void DescribeImage(StringBuilder sb) {
        if (_ddsFile.Header.PixelFormat.Flags != DdsPixelFormatFlags.FourCc)
            sb.Append(_ddsFile.Header.PixelFormat.Flags & ~DdsPixelFormatFlags.FourCc).Append("; ");

        sb.Append($"{_ddsFile.DataOffset + _ddsFile.Data.Length:##,###} Bytes");
        if (_ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.MipmapCount))
            sb.Append("; ").Append(_ddsFile.NumMipmaps).Append(" Mipmaps");
        sb.AppendLine();
        
        if (_ddsFile.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc)) {
            if (_ddsFile.Header.PixelFormat.Flags != DdsPixelFormatFlags.FourCc)
                sb.Append(_ddsFile.Header.PixelFormat.Flags & ~DdsPixelFormatFlags.FourCc).Append("; ");
            var uival = (uint) _ddsFile.Header.PixelFormat.FourCc;
            var c1 = (char) Math.Clamp((uival >> 0) & 0xFF, 0x20, 0x7F);
            var c2 = (char) Math.Clamp((uival >> 8) & 0xFF, 0x20, 0x7F);
            var c3 = (char) Math.Clamp((uival >> 16) & 0xFF, 0x20, 0x7F);
            var c4 = (char) Math.Clamp((uival >> 24) & 0xFF, 0x20, 0x7F);
            sb.AppendLine($"FourCC=0x{uival:X08}({c1}{c2}{c3}{c4})");
        } else {
            var inferredFourCc = _ddsFile.PixFmt.FourCc;
            if (inferredFourCc != _ddsFile.Header.PixelFormat.FourCc && inferredFourCc != DdsFourCc.Unknown) {
                var uival = (uint) inferredFourCc;
                var c1 = (char) Math.Clamp((uival >> 0) & 0xFF, 0x20, 0x7F);
                var c2 = (char) Math.Clamp((uival >> 8) & 0xFF, 0x20, 0x7F);
                var c3 = (char) Math.Clamp((uival >> 16) & 0xFF, 0x20, 0x7F);
                var c4 = (char) Math.Clamp((uival >> 24) & 0xFF, 0x20, 0x7F);
                sb.AppendLine($"FourCC(inferred)=0x{uival:X08}({c1}{c2}{c3}{c4})");
            }
        }

        if (_ddsFile.UseDxt10Header)
            sb.AppendLine($"DxgiFormat={_ddsFile.Dxt10Header.DxgiFormat} ({(int) _ddsFile.Dxt10Header.DxgiFormat})");
        else {
            var inferredDxgiFormat = _ddsFile.PixFmt.DxgiFormat;
            if (inferredDxgiFormat != DxgiFormat.Unknown &&
                (!_ddsFile.UseDxt10Header || inferredDxgiFormat != _ddsFile.Dxt10Header.DxgiFormat)) {
                sb.AppendLine($"DxgiFormat(inferred)={inferredDxgiFormat} ({(int) inferredDxgiFormat})");
            }
        }

        var inferredWicPixelFormat = _ddsFile.PixFmt.WicFormat;
        if (inferredWicPixelFormat != WicPixelFormat.GUID_WICPixelFormatUndefined) {
            var x = WicPixelFormat.FromClsid(inferredWicPixelFormat);
            sb.AppendLine($"WicPixelFormat(inferred)={x.FriendlyName}");
        }

        var useW = _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Width);
        var useH = _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Height);
        var useD = _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Depth);

        var isStandardCube = useW && useH && !useD && _ddsFile.NumFaces == 6;
        var isStandard1D = useW && !useH && !useD && !_ddsFile.IsCubeMap;
        var isStandard2D = useW && useH && !useD && !_ddsFile.IsCubeMap;
        var isStandard3D = useW && useH && useD && !_ddsFile.IsCubeMap;
        var isNonstandard = !isStandardCube && !isStandard1D && !isStandard2D && !isStandard3D;
        if (isStandard1D)
            sb.Append("1D: ").Append(_ddsFile.Header.Width);
        else if (isStandard2D)
            sb.Append("2D: ").Append(_ddsFile.Header.Width)
                .Append(" x ").Append(_ddsFile.Header.Height);
        else if (isStandard3D)
            sb.Append("3D: ").Append(_ddsFile.Header.Width)
                .Append(" x ").Append(_ddsFile.Header.Height)
                .Append(" x ").Append(_ddsFile.Header.Depth);
        else if (isStandardCube)
            sb.Append("Cube: ").Append(_ddsFile.Header.Width)
                .Append(" x ").Append(_ddsFile.Header.Height);
        else {
            sb.Append("Nonstandard;");
            if (useW) sb.Append(" W=").Append(_ddsFile.Header.Width);
            if (useH) sb.Append(" H=").Append(_ddsFile.Header.Height);
            if (useD) sb.Append(" D=").Append(_ddsFile.Header.Depth);
        }

        if (_ddsFile.UseDxt10Header && _ddsFile.Dxt10Header.ArraySize != 1)
            sb.Append($" [{_ddsFile.Dxt10Header.ArraySize}]");

        if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.Volume))
            sb.Append("; Volume");

        sb.AppendLine();

        if (isNonstandard && IsCubeMap) {
            sb.Append("Cube Faces: ");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveX))
                sb.Append(" +X");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeX))
                sb.Append(" -X");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveY))
                sb.Append(" +Y");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeY))
                sb.Append(" -Y");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveZ))
                sb.Append(" +Z");
            if (_ddsFile.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeZ))
                sb.Append(" -Z");
            sb.AppendLine();
        }

        if (_mipmap > 0) {
            sb.AppendLine().Append("Mipmap #").Append(_mipmap + 1).Append(": ");
            if (isStandard1D)
                sb.Append(WidthOfMipmap(0, _mipmap));
            else if (isStandard2D)
                sb.Append(WidthOfMipmap(0, _mipmap))
                    .Append(" x ").Append(HeightOfMipmap(0, _mipmap));
            else if (isStandard3D)
                sb.Append(WidthOfMipmap(0, _mipmap))
                    .Append(" x ").Append(HeightOfMipmap(0, _mipmap))
                    .Append(" x ").Append(NumSlicesOfMipmap(0, _mipmap));
            else if (isStandardCube)
                sb.Append(WidthOfMipmap(0, _mipmap))
                    .Append(" x ").Append(HeightOfMipmap(0, _mipmap));
            else {
                if (useW) sb.Append(" W=").Append(WidthOfMipmap(0, _mipmap));
                if (useH) sb.Append(" H=").Append(HeightOfMipmap(0, _mipmap));
                if (useD) sb.Append(" D=").Append(_ddsFile.DepthOrNumFaces(_mipmap));
            }

            sb.AppendLine();
        }
    }

    private void Relayout() {
        var width = WidthOfMipmap(_imageIndex, _mipmap);
        var height = HeightOfMipmap(_imageIndex, _mipmap);
        var slices = NumSlicesOfMipmap(_imageIndex, _mipmap);

        Layout = IGridLayout.CreateGridLayoutForDepthView(0, _mipmap, width, height, slices, IsCubeMap, _sliceSpacing);
    }
}
