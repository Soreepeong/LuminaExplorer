using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public class PlainBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Stream? _stream;
    private ResultDisposingTask<ComPtr<IWICBitmapSource>>? _wicBitmap;
    private ResultDisposingTask<Bitmap>? _bitmap;

    private Size _sliceSpacing;
    private bool _disposed;

    public PlainBitmapSource(string name, long size, Stream stream, Size sliceSpacing = new())
    {
        this.FileName = name;
        this.FileSize = size;
        this._stream = stream;

        unsafe {
            using var decoder = new ComPtr<IWICBitmapDecoder>();
            using var s = ManagedIStream.Create(stream, true);
            ImagingExtensions.WicFactory.Get()->CreateDecoderFromStream(
                s.Get(),
                null,
                WICDecodeOptions.WICDecodeMetadataCacheOnDemand,
                decoder.GetAddressOf()).Ensure();

            using var frame = new ComPtr<IWICBitmapFrameDecode>();
            decoder.Get()->GetFrame(0, frame.GetAddressOf()).Ensure();
            this._wicBitmap = new(Task.FromResult<ComPtr<IWICBitmapSource>>(new((IWICBitmapSource*) frame.Get())));
        }

        this._sliceSpacing = sliceSpacing;

        this.Layout = null!;
        this.Relayout();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._cancellationTokenSource.Cancel();

        SafeDispose.One(ref this._wicBitmap);
        SafeDispose.One(ref this._bitmap);
        SafeDispose.One(ref this._stream);

        this._cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;
        this._disposed = true;
        await this._cancellationTokenSource.CancelAsync();

        await Task.WhenAll(
            SafeDispose.OneAsync(ref this._wicBitmap),
            SafeDispose.OneAsync(ref this._bitmap));

        this._cancellationTokenSource.Dispose();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
    }

    public event Action? LayoutChanged;

    public string FileName { get; }

    public long FileSize { get; }

    public int ImageCount => 1;

    public IGridLayout Layout { get; private set; }

    public bool IsCubeMap => false;

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
        get => 0;
        set {
            if (value != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, null);
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
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (this._wicBitmap ??= new(
            Task.Run(
                () => this._bitmap!.Result.TryToWicBitmap(out var b, out var e) ? b : throw e,
                this._cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._wicBitmap?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice)
    {
        if (this._disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return (this._bitmap ??= new(
            Task.Run(
                () => this._wicBitmap!.Result.TryToGdipBitmap(out var b, out var e) ? b : throw e,
                this._cancellationTokenSource.Token))).Task;
    }

    public bool HasGdipBitmap(int imageIndex, int mipmap, int slice)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return this._bitmap?.IsCompletedSuccessfully is true;
    }

    public int NumberOfMipmaps(int imageIndex)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return 1;
    }

    public int WidthOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        if (this._bitmap?.IsCompletedSuccessfully is true)
            return this._bitmap.Result.Width;
        unsafe {
            uint width, height;
            this._wicBitmap!.Result.Get()->GetSize(&width, &height).Ensure();
            return (int) width;
        }
    }

    public int HeightOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        if (this._bitmap?.IsCompletedSuccessfully is true)
            return this._bitmap.Result.Width;
        unsafe {
            uint width, height;
            this._wicBitmap!.Result.Get()->GetSize(&width, &height).Ensure();
            return (int) height;
        }
    }

    public int NumSlicesOfMipmap(int imageIndex, int mipmap)
    {
        if (imageIndex != 0)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        if (mipmap != 0)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);
        return 1;
    }

    public void WriteTexFile(Stream stream) => throw new NotImplementedException();

    public void WriteDdsFile(Stream stream) => throw new NotImplementedException();

    public void DescribeImage(StringBuilder sb)
    {
        sb.AppendLine($"{this.FileSize:##,###} Bytes");

        sb.Append("2D: ").Append(this.WidthOfMipmap(0, 0))
            .Append(" x ").Append(this.HeightOfMipmap(0, 0))
            .AppendLine();
    }

    private void Relayout()
    {
        this.Layout = IGridLayout.CreateGridLayoutForDepthView(
            0,
            0,
            this.WidthOfMipmap(0, 0),
            this.HeightOfMipmap(0, 0),
            1,
            false,
            this._sliceSpacing);
        this.LayoutChanged?.Invoke();
    }
}
