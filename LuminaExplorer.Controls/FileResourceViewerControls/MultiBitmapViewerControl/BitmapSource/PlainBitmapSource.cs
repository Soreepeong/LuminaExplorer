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
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public class PlainBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Stream? _stream;
    private ResultDisposingTask<WicBitmapSource>? _wicBitmap;
    private ResultDisposingTask<Bitmap>? _bitmap;

    private Size _sliceSpacing;
    private bool _disposed;

    public PlainBitmapSource(string name, long size, Stream stream, Size sliceSpacing = new()) {
        FileName = name;
        FileSize = size;
        _stream = stream;

        using (var decoder = WICImagingFactory.CreateDecoderFromStream(stream)) {
            decoder.Object.GetFrame(0, out var pFrame).ThrowOnError();
            _wicBitmap = new(Task.FromResult(new WicBitmapSource(pFrame)));
        }

        _sliceSpacing = sliceSpacing;

        Layout = null!;
        Relayout();
    }

    public void Dispose() {
        if (_disposed)
            return;
        
        _disposed = true;
        _cancellationTokenSource.Cancel();

        SafeDispose.One(ref _wicBitmap);
        SafeDispose.One(ref _bitmap);
        SafeDispose.One(ref _stream);

        _cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed)
            return;
        _disposed = true;
        _cancellationTokenSource.Cancel();

        await Task.WhenAll(
            SafeDispose.OneAsync(ref _wicBitmap),
            SafeDispose.OneAsync(ref _bitmap));

        _cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public string FileName { get; }

    public long FileSize { get; }

    public int ImageCount => 1;

    public IGridLayout Layout { get; private set; }

    public bool IsCubeMap => false;

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
        get => 0;
        set {
            if (value != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
        }
    }

    public void UpdateSelection(int imageIndex, int mipmap) {
        ImageIndex = imageIndex;
        Mipmap = mipmap;
    }

    public Task<WicBitmapSource> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (_wicBitmap ??= new(Task.Run(
            () => _bitmap!.Result.TryToWicBitmap(out var b, out var e) ? b : throw e,
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return _wicBitmap?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (_bitmap ??= new(Task.Run(
            () => _wicBitmap!.Result.TryToGdipBitmap(out var b, out var e) ? b : throw e,
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return _bitmap?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return 1;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return _bitmap?.IsCompletedSuccessfully is true ? _bitmap.Result.Width : _wicBitmap!.Result.Width;
    }

    public int HeightOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return _bitmap?.IsCompletedSuccessfully is true ? _bitmap.Result.Height : _wicBitmap!.Result.Height;
    }

    public int NumSlicesOfMipmap(int imageIndex, int mipmap) {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return 1;
    }

    public void WriteTexFile(Stream stream) => throw new NotImplementedException();

    public void WriteDdsFile(Stream stream) => throw new NotImplementedException();

    public void DescribeImage(StringBuilder sb) {
        sb.AppendLine($"{FileSize:##,###} Bytes");

        sb.Append("2D: ").Append(WidthOfMipmap(0, 0))
            .Append(" x ").Append(HeightOfMipmap(0, 0))
            .AppendLine();
    }

    private void Relayout() {
        Layout = IGridLayout.CreateGridLayoutForDepthView(
            0,
            0,
            WidthOfMipmap(0, 0),
            HeightOfMipmap(0, 0),
            1,
            false,
            _sliceSpacing);
        LayoutChanged?.Invoke();
    }
}
