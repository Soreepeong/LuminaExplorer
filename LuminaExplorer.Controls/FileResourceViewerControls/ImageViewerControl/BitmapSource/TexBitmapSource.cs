using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;

public sealed class TexBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly bool _isCube;
    private TexFile _texFile;
    private ResultDisposingTask<WicBitmapSource>?[ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _mipmap;
    private bool _disposed;

    public TexBitmapSource(TexFile texFile, int mipmap = 0, Size sliceSpacing = new()) {
        if (mipmap < 0 || mipmap > texFile.Header.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        _texFile = texFile;
        _wicBitmaps = new ResultDisposingTask<WicBitmapSource>[_texFile.Header.MipLevels][];
        _bitmaps = new ResultDisposingTask<Bitmap>[_texFile.Header.MipLevels][];
        for (var i = 0; i < _texFile.Header.MipLevels; i++) {
            var mipDepth = texFile.TextureBuffer.DepthOfMipmap(i);
            _wicBitmaps[i] = new ResultDisposingTask<WicBitmapSource>?[mipDepth];
            _bitmaps[i] = new ResultDisposingTask<Bitmap>?[mipDepth];
        }

        _isCube = _texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube);

        _sliceSpacing = sliceSpacing;

        Layout = null!;
        _mipmap = -1;
        UpdateSelection(0, mipmap);
    }

    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _texFile = null!;
        _cancellationTokenSource.Cancel();

        SafeDispose.Enumerable(ref _wicBitmaps!);
        SafeDispose.Enumerable(ref _bitmaps!);

        _cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;
        _disposed = true;
        _texFile = null!;
        _cancellationTokenSource.Cancel();

        await Task.WhenAll(
            SafeDispose.EnumerableAsync(ref _wicBitmaps!),
            SafeDispose.EnumerableAsync(ref _bitmaps!));

        _cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public int ImageCount => 1;
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
        get => 0;
        set {
            if (value != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }
    }

    public int Mipmap {
        get => _mipmap;
        set {
            if (_mipmap == value)
                return;
            if (value < 0 || value >= _texFile.Header.MipLevels)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            _mipmap = value;
            Relayout();
            LayoutChanged?.Invoke();
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap) {
        ImageIndex = imageIndex;
        Mipmap = mipmap;
    }

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_wicBitmaps[_mipmap][slice] ??= new(Task.Run(
            () => {
                WicBitmapSource? wb = null;
                try {
                    wb = _texFile.ToWicBitmap(_mipmap, slice);
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    wb.ConvertTo(
                        WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                        paletteTranslate: WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                    return wb;
                } catch (Exception) {
                    wb?.Dispose();
                    throw;
                }
            },
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _wicBitmaps[_mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_bitmaps[mipmap][slice] ??= new(Task.Run(
            () => {
                if (_wicBitmaps[_mipmap][slice] is {Task.IsCompletedSuccessfully: true} wicBitmapTask) {
                    if (wicBitmapTask.Result.TryToGdipBitmap(out var b))
                        return b;
                }
                
                var texBuf = _texFile.TextureBuffer.Filter(_mipmap, slice, TexFile.TextureFormat.B8G8R8A8);
                unsafe {
                    fixed (void* p = texBuf.RawData) {
                        using var b = new Bitmap(
                            texBuf.Width,
                            texBuf.Height,
                            4 * texBuf.Width,
                            PixelFormat.Format32bppArgb,
                            (nint) p);
                        return new(b);
                    }
                }
            },
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _bitmaps[mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _texFile.Header.MipLevels;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _texFile.TextureBuffer.WidthOfMipmap(mipmap);
    }

    public int HeightOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _texFile.TextureBuffer.HeightOfMipmap(mipmap);
    }

    public int DepthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _texFile.TextureBuffer.DepthOfMipmap(mipmap);
    }

    private void Relayout() {
        var width = _texFile.TextureBuffer.WidthOfMipmap(_mipmap);
        var height = _texFile.TextureBuffer.HeightOfMipmap(_mipmap);
        var depth = _texFile.TextureBuffer.DepthOfMipmap(_mipmap);

        Layout = IGridLayout.CreateGridLayoutForDepthView(0, _mipmap, width, height, depth, _isCube, _sliceSpacing);
    }
}
