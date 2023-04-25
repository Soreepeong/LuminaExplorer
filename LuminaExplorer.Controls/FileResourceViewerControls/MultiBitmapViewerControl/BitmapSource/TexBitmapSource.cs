using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DirectN;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public sealed class TexBitmapSource : IBitmapSource {
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly bool _isCube;
    private TexFile _texFile;
    private ResultDisposingTask<IComObject<IWICBitmapSource>>?[ /* Mip */][ /* Slice */] _wicBitmaps;
    private ResultDisposingTask<Bitmap>?[ /* Mip */][ /* Slice */] _bitmaps;

    private Size _sliceSpacing;
    private int _mipmap;
    private bool _disposed;

    public TexBitmapSource(TexFile texFile, int mipmap = 0, Size sliceSpacing = new()) {
        if (mipmap < 0 || mipmap >= texFile.Header.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, null);

        _texFile = texFile;
        _wicBitmaps = new ResultDisposingTask<IComObject<IWICBitmapSource>>[_texFile.Header.MipLevels][];
        _bitmaps = new ResultDisposingTask<Bitmap>[_texFile.Header.MipLevels][];
        for (var i = 0; i < _texFile.Header.MipLevels; i++) {
            var mipDepth = texFile.TextureBuffer.DepthOfMipmap(i);
            _wicBitmaps[i] = new ResultDisposingTask<IComObject<IWICBitmapSource>>?[mipDepth];
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

    public string FileName => Path.GetFileName(_texFile.FilePath.Path);
    
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

    public Task<IComObject<IWICBitmapSource>> GetWicBitmapSourceAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_wicBitmaps[mipmap][slice] ??= new(Task.Run(
            () => _texFile.ToWicBitmapSource(mipmap, slice),
            _cancellationTokenSource.Token))).Task;
    }

    public bool HasWicBitmapSource(int imageIndex, int mipmap, int slice) {
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return _wicBitmaps[mipmap][slice]?.IsCompletedSuccessfully is true;
    }

    Task<Bitmap> IBitmapSource.GetGdipBitmapAsync(int imageIndex, int mipmap, int slice) {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TexBitmapSource));
        if (imageIndex != 0 || mipmap < 0 || mipmap >= NumberOfMipmaps(imageIndex))
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);
        return (_bitmaps[mipmap][slice] ??= new(Task.Run(
            () => {
                if (_wicBitmaps[mipmap][slice] is {Task.IsCompletedSuccessfully: true} wicBitmapTask) {
                    if (wicBitmapTask.Result.TryToGdipBitmap(out var b, out _))
                        return b;
                }

                var texBuf = _texFile.TextureBuffer.Filter(mipmap, slice, TexFile.TextureFormat.B8G8R8A8);
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

    public void WriteTexFile(Stream stream) => stream.Write(_texFile.Data);

    public void WriteDdsFile(Stream stream) {
        using var ms = new DdsFile(Path.GetFileNameWithoutExtension(_texFile.FilePath.Path) + ".dds", _texFile)
            .CreateStream();
        ms.CopyTo(stream);
    }

    public void DescribeImage(StringBuilder sb) {
        sb.Append(_texFile.Header.Format).Append("; ")
            .Append($"{_texFile.Data.Length:##,###} Bytes");
        if (_texFile.Header.MipLevels > 1)
            sb.Append("; ").Append(_texFile.Header.MipLevels).Append(" mipmaps");
        sb.AppendLine();

        if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
            sb.Append("1D: ").Append(_texFile.Header.Width);
        if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
            sb.Append("2D: ").Append(_texFile.Header.Width)
                .Append(" x ").Append(_texFile.Header.Height);
        if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
            sb.Append("3D: ").Append(_texFile.Header.Width)
                .Append(" x ").Append(_texFile.Header.Height)
                .Append(" x ").Append(_texFile.Header.Depth);
        if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            sb.Append("Cube: ").Append(_texFile.Header.Width)
                .Append(" x ").Append(_texFile.Header.Height);
        if (_mipmap > 0) {
            sb.AppendLine().Append("Mipmap #").Append(_mipmap + 1).Append(": ");
            if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                sb.Append(_texFile.TextureBuffer.WidthOfMipmap(_mipmap));
            if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                sb.Append(_texFile.TextureBuffer.WidthOfMipmap(_mipmap))
                    .Append(" x ").Append(_texFile.TextureBuffer.HeightOfMipmap(_mipmap));
            if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                sb.Append(_texFile.TextureBuffer.WidthOfMipmap(_mipmap))
                    .Append(" x ").Append(_texFile.TextureBuffer.HeightOfMipmap(_mipmap))
                    .Append(" x ").Append(_texFile.TextureBuffer.DepthOfMipmap(_mipmap));
            if (_texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                sb.Append(_texFile.TextureBuffer.WidthOfMipmap(_mipmap))
                    .Append(" x ").Append(_texFile.TextureBuffer.HeightOfMipmap(_mipmap));
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
                     TexFile.Attribute.TextureNoSwizzle
                 })
            if (_texFile.Header.Type.HasFlag(f))
                sb.Append("+ ").AppendLine(f.ToString());
    }

    private void Relayout() {
        var width = _texFile.TextureBuffer.WidthOfMipmap(_mipmap);
        var height = _texFile.TextureBuffer.HeightOfMipmap(_mipmap);
        var depth = _texFile.TextureBuffer.DepthOfMipmap(_mipmap);

        Layout = IGridLayout.CreateGridLayoutForDepthView(0, _mipmap, width, height, depth, _isCube, _sliceSpacing);
    }

    public static TexFile FromFile(FileInfo fileInfo) {
        var data = File.ReadAllBytes(fileInfo.FullName);
        
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var file = (TexFile) Activator.CreateInstance(typeof(TexFile))!;
        typeof(FileResource)
            .GetProperty("FilePath", bindingFlags)
            !.SetValue(file, fileInfo.FullName);
        typeof(FileResource)
            .GetProperty("Data", bindingFlags)
            !.SetValue(file, data);
        typeof(FileResource)
            .GetProperty("Reader", bindingFlags)
            !.SetValue(file, new LuminaBinaryReader(data));
        typeof(FileResource)
            .GetMethod("LoadFile", bindingFlags)
            !.Invoke(file, null);
        return file;
    
    }
}
