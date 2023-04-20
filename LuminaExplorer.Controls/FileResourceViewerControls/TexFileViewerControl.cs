using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using WicNet;
using Rectangle = System.Drawing.Rectangle;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private readonly BufferedGraphicsContext _context = new();
    private D2DRenderer? _test;

    public readonly PanZoomTracker Viewport;

    private TextureBuffer? _textureBuffer;
    private Bitmap? _bitmap;
    private int _currentDepth;
    private int _currentMipmap;
    private Color _borderColor = Color.LightGray;
    private int _transparencyCellSize = 8;

    public TexFileViewerControl() {
        MouseActivity.UseLeftDrag = MouseActivity.UseMiddleDrag = MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;

        Viewport = new(MouseActivity);
        Viewport.ViewportChanged += Invalidate;

        try {
            _test = new(this);
        } catch (Exception) {
            // swallow
        }
    }

    public Color BorderColor {
        get => _borderColor;
        set {
            if (_borderColor == value)
                return;
            _borderColor = value;
            Invalidate();
            if (_test is not null) {
                try {
                    _test.BorderColor = value;
                } catch (Exception) {
                    _test.Dispose();
                    _test = null;
                }
            }
        }
    }

    public int TransparencyCellSize {
        get => _transparencyCellSize;
        set {
            if (_transparencyCellSize == value)
                return;
            _transparencyCellSize = value;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _context.Dispose();
            Viewport.Dispose();
            _test?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (_test is not null) {
            try {
                _test.Draw();
                return;
            } catch (Exception) {
                _test.Dispose();
                _test = null;

                UpdateBitmap(_currentDepth, _currentMipmap, true);
            }
        }

        using var buffer = _context.Allocate(e.Graphics, e.ClipRectangle);
        var g = buffer.Graphics;

        using var backBrush = new SolidBrush(BackColor);

        g.FillRectangle(backBrush, e.ClipRectangle);
        var cellSize = TransparencyCellSize;
        if (cellSize > 0) {
            var controlSize = Size;
            var c1 = false;
            using var cellBrush = new SolidBrush(Color.LightGray);
            for (var i = 0; i < controlSize.Width; i += cellSize) {
                var c2 = c1;
                c1 = !c1;
                for (var j = 0; j < controlSize.Height; j += cellSize) {
                    if (c2)
                        g.FillRectangle(cellBrush, i, j, i + cellSize, j + cellSize);

                    c2 = !c2;
                }
            }
        }

        if (_bitmap is not { } bitmap) {
            buffer.Render();
            return;
        }

        var imageRect = Viewport.EffectiveRect;
        var insetRect = new Rectangle(
            Padding.Left,
            Padding.Top,
            Width - Padding.Left - Padding.Right,
            Height - Padding.Bottom - Padding.Top);

        g.DrawImage(bitmap, imageRect);
        using (var borderPen = new Pen(Color.LightGray))
            g.DrawRectangle(borderPen, Rectangle.Inflate(imageRect, 1, 1));

        var stringFormat = new StringFormat {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.None,
        };

        var zoomText = $"Zoom {Viewport.EffectiveZoom * 100:0.00}%";
        for (var i = -2; i <= 2; i++) {
            for (var j = -2; j <= 2; j++) {
                if (i == 0 && j == 0)
                    continue;
                g.DrawString(
                    zoomText,
                    Font,
                    backBrush,
                    insetRect with {Width = insetRect.Width + i, Height = insetRect.Height + j},
                    stringFormat);
            }
        }

        using var foreBrush = new SolidBrush(ForeColor);
        g.DrawString(
            zoomText,
            Font,
            foreBrush,
            insetRect,
            stringFormat);

        buffer.Render();
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        // intentionally left empty
    }

    protected override void OnForeColorChanged(EventArgs e) {
        base.OnForeColorChanged(e);
        if (_test is not null) {
            try {
                _test.ForeColor = ForeColor;
            } catch (Exception) {
                _test.Dispose();
                _test = null;
            }
        }
    }

    protected override void OnBackColorChanged(EventArgs e) {
        base.OnBackColorChanged(e);
        if (_test is not null) {
            try {
                _test.BackColor = BackColor;
            } catch (Exception) {
                _test.Dispose();
                _test = null;
            }
        }
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        _test?.BitmapSize ?? _bitmap?.Size ?? base.GetPreferredSize(proposedSize);

    public void UpdateBitmap(int depth, int mipmap, bool force = false) {
        if (_textureBuffer is null || (_currentDepth == depth && _currentMipmap == mipmap))
            return;
        _currentDepth = _currentMipmap = 0;

        _bitmap?.Dispose();

        if (_test is not null) {
            try {
                try {
                    _test.LoadDds(FileResourceTyped!.ToDds(), mipmap, depth);
                    // TODO: make ToDds reliable so that the below becomes unnecessary
                } catch (Exception) {
                    _test.LoadTexFile(FileResourceTyped!, mipmap, depth);
                }

                Viewport.Reset(_test.BitmapSize);
                return;
            } catch (Exception e) {
                Debug.WriteLine(e);
                _test.Dispose();
                _test = null;
            }
        }

        var width = _textureBuffer.WidthOfMipmap(mipmap);
        var height = _textureBuffer.HeightOfMipmap(mipmap);
        var mipmapOffset = _textureBuffer.MipmapAllocations.Take(mipmap).Sum();
        unsafe {
            fixed (void* p = _textureBuffer.RawData) {
                using var b = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppArgb,
                    (nint) p + mipmapOffset + 4 * width * height * depth);
                _bitmap = new(b);
            }
        }

        Viewport.Reset(_bitmap.Size);
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        _textureBuffer = FileResourceTyped!.TextureBuffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
        UpdateBitmap(0, 0);
    }

    public override void ClearFile() {
        ClearFileImpl();
        base.ClearFile();
    }

    private void ClearFileImpl() {
        _bitmap?.Dispose();
        _bitmap = null;
        _test?.UnloadImage();
        _textureBuffer = null;
        _currentDepth = _currentMipmap = -1;
        Viewport.Reset(new());
    }

    private sealed class D2DRenderer : IDisposable {
        private static D2D? _d2dApi;
        private static Exception? _apiInitializationException;

        private readonly TexFileViewerControl _control;
        private ComPtr<ID2D1HwndRenderTarget> _renderTarget;
        private ComPtr<ID2D1Bitmap> _bitmap;
        private ComPtr<ID2D1SolidColorBrush> _foreColorBrush;
        private ComPtr<ID2D1SolidColorBrush> _backColorBrush;
        private ComPtr<ID2D1SolidColorBrush> _borderColorBrush;
        private Color _foreColor;
        private Color _backColor;
        private Color _borderColor;

        public unsafe D2DRenderer(TexFileViewerControl control) {
            _control = control;

            try {
                if (_apiInitializationException is not null)
                    throw _apiInitializationException;

                try {
                    _d2dApi ??= D2D.GetApi();
                } catch (Exception e) {
                    _apiInitializationException = e;
                    throw;
                }

                void* dfactory = null;
                var guid = ID2D1Factory.Guid;
                var fo = new FactoryOptions();
                Marshal.ThrowExceptionForHR(_d2dApi.D2D1CreateFactory(FactoryType.SingleThreaded, ref guid, fo,
                    ref dfactory));
                using var d2d1Factory = new ComPtr<ID2D1Factory>((ID2D1Factory*) dfactory);

                var rto = new RenderTargetProperties();
                var hrto = new HwndRenderTargetProperties(
                    hwnd: _control.Handle,
                    pixelSize: new((uint) _control.Width, (uint) _control.Height));

                for (var i = 0; i < 10 && _renderTarget.Handle is null; i++)
                    Marshal.ThrowExceptionForHR(d2d1Factory.CreateHwndRenderTarget(rto, hrto, ref _renderTarget));

                if (_renderTarget.Handle is null)
                    throw new("??");
                ForeColor = _control.ForeColor;
                BackColor = _control.BackColor;
                BorderColor = Color.LightGray;
                _control.Resize += ControlOnResize;
            } catch (Exception) {
                Dispose();
                throw;
            }
        }

        public Size BitmapSize { get; private set; }

        public Color ForeColor {
            get => _foreColor;
            set {
                if (_foreColor == value)
                    return;
                _foreColor = value;

                unsafe {
                    _foreColorBrush.Dispose();
                    _foreColorBrush = null;
                    Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                        new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                        null,
                        ref _foreColorBrush));
                }
            }
        }

        public Color BackColor {
            get => _backColor;
            set {
                if (_backColor == value)
                    return;
                _backColor = value;

                unsafe {
                    _backColorBrush.Dispose();
                    _backColorBrush = null;
                    Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                        new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                        null,
                        ref _backColorBrush));
                }
            }
        }

        public Color BorderColor {
            get => _borderColor;
            set {
                if (_borderColor == value)
                    return;
                _borderColor = value;

                unsafe {
                    _borderColorBrush.Dispose();
                    _borderColorBrush = null;
                    Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                        new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                        null,
                        ref _borderColorBrush));
                }
            }
        }

        private void ReleaseUnmanagedResources() {
            _foreColorBrush.Dispose();
            _foreColorBrush = null;
            _backColorBrush.Dispose();
            _backColorBrush = null;
            _bitmap.Dispose();
            _bitmap = null;
            _renderTarget.Dispose();
            _renderTarget = null;
        }

        public void Dispose() {
            ReleaseUnmanagedResources();
            _control.Resize -= ControlOnResize;
            GC.SuppressFinalize(this);
        }

        ~D2DRenderer() {
            ReleaseUnmanagedResources();
        }

        public unsafe void LoadDds(Stream stream, int mipIndex, int depth) {
            using var decoder = DirectN.WICImagingFactory.CreateDecoderFromStream(
                stream,
                WicImagingComponent.CLSID_WICDdsDecoder);
            using var ddsDecoder = decoder.AsComObject<DirectN.IWICDdsDecoder>();

            ddsDecoder.Object.GetFrame(0, (uint) mipIndex, (uint) depth, out var pFrame).ThrowOnError();
            using var source = new WicBitmapSource(pFrame);

            using var converter = DirectN.WICImagingFactory.WithFactory(x => {
                x.CreateFormatConverter(out var y).ThrowOnError();
                return new DirectN.ComObject<DirectN.IWICFormatConverter>(y);
            });
            converter.Object.Initialize(source.ComObject.Object, WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                DirectN.WICBitmapDitherType.WICBitmapDitherTypeNone,
                null, 0, DirectN.WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut).ThrowOnError();
            ComPtr<ID2D1Bitmap> newBitmap = null;
            Marshal.ThrowExceptionForHR(_renderTarget.CreateBitmapFromWicBitmap(
                (IWICBitmapSource*) converter.GetInterfacePointer<DirectN.IWICBitmapSource>().ToPointer(),
                null,
                ref newBitmap));

            _bitmap.Release();
            _bitmap = newBitmap;
            var ps = _bitmap.GetPixelSize();
            BitmapSize = new((int) ps.X, (int) ps.Y);
        }

        public unsafe void LoadTexFile(TexFile texFile, int mipIndex, int depth) {
            var texBuf = texFile.TextureBuffer.Filter(mip: mipIndex, z: depth, TexFile.TextureFormat.B8G8R8A8);

            ComPtr<ID2D1Bitmap> newBitmap = null;
            var bp = new BitmapProperties(pixelFormat: new(
                Format.FormatB8G8R8A8Unorm,
                Silk.NET.Direct2D.AlphaMode.Premultiplied));
            var buf = (byte[]) texBuf.RawData.Clone();
            for (var i = 0; i < buf.Length; i += 4) {
                var a = buf[i + 3];
                switch (a) {
                    case 0:
                        buf[i] = buf[i + 1] = buf[i + 2] = 0;
                        break;
                    case 255:
                        continue;
                    default:
                        buf[i] = (byte) (buf[i] * a / 255);
                        buf[i + 1] = (byte) (buf[i + 1] * a / 255);
                        buf[i + 2] = (byte) (buf[i + 2] * a / 255);
                        continue;
                }
            }

            fixed (void* p = buf)
                Marshal.ThrowExceptionForHR(_renderTarget.CreateBitmap(
                    new((uint) texBuf.Width, (uint) texBuf.Height),
                    p,
                    (uint) texBuf.Width * 4,
                    &bp,
                    ref newBitmap));

            _bitmap.Release();
            _bitmap = newBitmap;
            var ps = _bitmap.GetPixelSize();
            BitmapSize = new((int) ps.X, (int) ps.Y);
        }

        public void UnloadImage() {
            _bitmap.Release();
            _bitmap = null;
            BitmapSize = new();
        }

        public unsafe void Draw() {
            _renderTarget.BeginDraw();
            _renderTarget.FillRectangle(_control.ClientRectangle.ToSilkFloat(), _backColorBrush);
            if (_bitmap.Handle is null) {
                Marshal.ThrowExceptionForHR(_renderTarget.EndDraw(new Span<ulong>(), new Span<ulong>()));
                return;
            }

            var imageRect = _control.Viewport.EffectiveRect;
            var insetRect = new Rectangle(
                _control.Padding.Left,
                _control.Padding.Top,
                _control.Width - _control.Padding.Left - _control.Padding.Right,
                _control.Height - _control.Padding.Bottom - _control.Padding.Top);

            if (_bitmap.Handle is not null) {
                var cellSize = _control.TransparencyCellSize;
                if (cellSize > 0) {
                    var controlSize = _control.Size;
                    var c1 = false;
                    for (var i = 0; i < controlSize.Width; i += cellSize) {
                        var c2 = c1;
                        c1 = !c1;
                        for (var j = 0; j < controlSize.Height; j += cellSize) {
                            if (c2) {
                                _renderTarget.FillRectangle(
                                    new Box2D<float>(i, j, i + cellSize, j + cellSize),
                                    _borderColorBrush);
                            }

                            c2 = !c2;
                        }
                    }
                }

                _renderTarget.DrawBitmap(
                    _bitmap,
                    imageRect.ToSilkFloat(),
                    1f, // opacity
                    BitmapInterpolationMode.Linear,
                    new Box2D<float>(0, 0, BitmapSize.Width, BitmapSize.Height));

                _renderTarget.DrawRectangle(
                    Rectangle.Inflate(imageRect, 1, 1).ToSilkFloat(),
                    _borderColorBrush,
                    1f, // stroke width
                    new ComPtr<ID2D1StrokeStyle>());

                // TODO: draw text information
            }

            Marshal.ThrowExceptionForHR(_renderTarget.EndDraw(new Span<ulong>(), new Span<ulong>()));
        }

        // Ignore errors, as it'll be thrown again on EndDraw
        private void ControlOnResize(object? sender, EventArgs e) {
            _renderTarget.Resize(new Vector2D<uint>((uint) _control.Width, (uint) _control.Height));
            _control.Invalidate();
        }
    }
}
