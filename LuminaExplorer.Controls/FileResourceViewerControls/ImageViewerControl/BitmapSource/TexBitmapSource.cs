using System;
using System.Drawing;
using System.Linq;
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
    private readonly TexFile _texFile;
    private readonly ResultDisposingTask<WicBitmapSource>?[ /* Mip */][ /* Slice */] _wicBitmaps;
    private readonly ResultDisposingTask<Bitmap>?[ /* Mip */][ /* Slice */] _bitmaps;
    private readonly bool _isCube;

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
        _cancellationTokenSource.Cancel();

        Task.WaitAll(
            Task.WhenAll(_wicBitmaps
                .SelectMany(x => x)
                .Select(x => x?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            ),
            Task.WhenAll(_bitmaps
                .SelectMany(x => x)
                .Select(x => x?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            )
        );

        _cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;
        _disposed = true;
        _cancellationTokenSource.Cancel();

        await Task.WhenAll(
            Task.WhenAll(_wicBitmaps
                .SelectMany(x => x)
                .Select(x => x?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            ),
            Task.WhenAll(_bitmaps
                .SelectMany(x => x)
                .Select(x => x?.DisposeAsync().AsTask() ?? Task.CompletedTask)
            )
        );

        _cancellationTokenSource.Dispose();
    }

    public event Action? ImageOrMipmapChanged;

    public int ImageCount => 1;
    public int NumMipmaps => _texFile.Header.MipLevels;
    public IGridLayout Layout { get; private set; }
    public int BaseWidth => _texFile.Header.Width;
    public int BaseHeight => _texFile.Header.Height;
    public int BaseDepth => _texFile.Header.Depth;

    public Size SliceSpacing {
        get => _sliceSpacing;
        set {
            if (_sliceSpacing == value)
                return;

            _sliceSpacing = value;
            Relayout();
        }
    }

    public int Width => _texFile.TextureBuffer.WidthOfMipmap(_mipmap);
    public int Height => _texFile.TextureBuffer.HeightOfMipmap(_mipmap);
    public int Depth => _texFile.TextureBuffer.DepthOfMipmap(_mipmap);

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
            if (value < 0 || value >= NumMipmaps)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);

            _mipmap = value;
            Relayout();
            ImageOrMipmapChanged?.Invoke();
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap) {
        ImageIndex = imageIndex;
        Mipmap = mipmap;
    }

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
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

    public bool HasWicBitmapSource(int slice) => _wicBitmaps[_mipmap][slice]?.IsCompletedSuccessfully is true;

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        return (_bitmaps[_mipmap][slice] ??= new(Task.Run(
            () => {
                var texBuf = _texFile.TextureBuffer.Filter(_mipmap, slice, TexFile.TextureFormat.B8G8R8A8);
                unsafe {
                    fixed (void* p = texBuf.RawData) {
                        using var b = new Bitmap(
                            texBuf.Width,
                            texBuf.Height,
                            4 * texBuf.Width,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                            (nint) p);
                        return new Bitmap(b);
                    }
                }
            },
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int slice) => _bitmaps[_mipmap][slice]?.IsCompletedSuccessfully is true;

    private void Relayout() {
        Layout = IGridLayout.CreateGridLayout(Width, Height, Depth, _isCube, _sliceSpacing);
    }
}
