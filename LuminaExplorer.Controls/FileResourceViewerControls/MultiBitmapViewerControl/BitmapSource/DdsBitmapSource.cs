using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DirectN;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public sealed class DdsBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly bool _isCube;
    private DdsFile _ddsFile;
    private ResultDisposingTask<IComObject<IWICBitmapSource>>?[ /* Image */][ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Image */][ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _imageIndex;
    private int _mipmap;
    private bool _disposed;

    public DdsBitmapSource(DdsFile ddsFile, int imageIndex = 0, int mipmap = 0, Size sliceSpacing = new()) {

        _ddsFile = ddsFile;
        ImageCount = _ddsFile.UseDxt10Header ? _ddsFile.Dxt10Header.ArraySize : 1;
        var numMips = ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.MipmapCount) ? ddsFile.Header.MipMapCount : 1;
        var baseDepth = ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Depth) ? ddsFile.Header.Depth : 1;

        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= numMips)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        _wicBitmaps = new ResultDisposingTask<IComObject<IWICBitmapSource>>[ImageCount][][];
        _bitmaps = new ResultDisposingTask<Bitmap>[ImageCount][][];
        for (var image = 0; image < ImageCount; image++) {
            var imageWicBitmaps =
                _wicBitmaps[image] = new ResultDisposingTask<IComObject<IWICBitmapSource>>?[numMips][];
            var imageBitmaps = _bitmaps[image] = new ResultDisposingTask<Bitmap>?[numMips][];
            for (var mip = 0; mip < numMips; mip++) {
                var mipDepth = Math.Max(1, baseDepth >> mip);
                imageWicBitmaps[mip] = new ResultDisposingTask<IComObject<IWICBitmapSource>>?[mipDepth];
                imageBitmaps[mip] = new ResultDisposingTask<Bitmap>?[mipDepth];
            }
        }

        _isCube = _ddsFile.Header.Caps2.HasFlag(DdsCaps2.Cubemap);

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

    public int ImageCount { get; }

    public IGridLayout Layout { get; private set; }

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

    public Task<IComObject<IWICBitmapSource>> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice) {
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
                if (!r.Result.TryToGdipBitmap(out var b, out var e))
                    throw e;
                return b;
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
        return _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.MipmapCount) ? _ddsFile.Header.MipMapCount : 1;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Width) ? _ddsFile.Header.Width : 1;
    }

    public int HeightOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Height) ? _ddsFile.Header.Height : 1;
    }

    public int DepthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex < 0 || imageIndex >= ImageCount)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _ddsFile.Header.Flags.HasFlag(DdsHeaderFlags.Depth) ? _ddsFile.Header.Depth : 1;
    }

    public void WriteTexFile(Stream stream) {
        throw new NotImplementedException();
    }

    public void WriteDdsFile(Stream stream) {
        using var ms = _ddsFile.CreateStream();
        ms.CopyTo(stream);
    }

    public void DescribeImage(StringBuilder sb) {
        sb.AppendLine("TODO"); // TODO
    }

    private void Relayout() {
        var width = WidthOfMipmap(_imageIndex, _mipmap);
        var height = HeightOfMipmap(_imageIndex, _mipmap);
        var depth = DepthOfMipmap(_imageIndex, _mipmap);

        Layout = IGridLayout.CreateGridLayoutForDepthView(0, _mipmap, width, height, depth, _isCube, _sliceSpacing);
    }
}
