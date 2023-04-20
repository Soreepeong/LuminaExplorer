using System.Runtime.InteropServices;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using WicNet;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed class D2DRenderer : ITexRenderer {
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

                // for some reason it returns success but renderTarget is null occasionally
                for (var i = 0; i < 10 && _renderTarget.Handle is null; i++)
                    Marshal.ThrowExceptionForHR(d2d1Factory.CreateHwndRenderTarget(rto, hrto, ref _renderTarget));

                if (_renderTarget.Handle is null)
                    throw new("Fail");

                ForeColor = _control.ForeColor;
                BackColor = _control.BackColor;
                BorderColor = Color.LightGray;
                _control.Resize += ControlOnResize;
            } catch (Exception e) {
                LastException = e;
            }
        }

        public unsafe bool HasImage => _bitmap.Handle is not null;

        public Exception? LastException { get; private set; }

        public Size Size { get; private set; }

        public Color ForeColor {
            get => _foreColor;
            set {
                if (_foreColor == value)
                    return;
                _foreColor = value;

                try {
                    unsafe {
                        _foreColorBrush.Dispose();
                        _foreColorBrush = null;
                        Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                            new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                            null,
                            ref _foreColorBrush));
                    }
                } catch (Exception e) {
                    LastException = e;
                }
            }
        }

        public Color BackColor {
            get => _backColor;
            set {
                if (_backColor == value)
                    return;
                _backColor = value;

                try {
                    unsafe {
                        _backColorBrush.Dispose();
                        _backColorBrush = null;
                        Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                            new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                            null,
                            ref _backColorBrush));
                    }
                } catch (Exception e) {
                    LastException = e;
                }
            }
        }

        public Color BorderColor {
            get => _borderColor;
            set {
                if (_borderColor == value)
                    return;
                _borderColor = value;

                try {
                    unsafe {
                        _borderColorBrush.Dispose();
                        _borderColorBrush = null;
                        Marshal.ThrowExceptionForHR(_renderTarget.CreateSolidColorBrush(
                            new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                            null,
                            ref _borderColorBrush));
                    }
                } catch (Exception e) {
                    LastException = e;
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

        public unsafe bool LoadTexFile(TexFile texFile, int mipIndex, int depth) {
            try {
                using var stream = texFile.ToDds();
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
                Size = new((int) ps.X, (int) ps.Y);
            } catch (Exception) {
                // TODO: make ToDds reliable so that the below becomes unnecessary
            }

            try {
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
                Size = new((int) ps.X, (int) ps.Y);
                return true;
            } catch (Exception e) {
                LastException = e;
                return false;
            }
        }

        public void Reset() {
            _bitmap.Release();
            _bitmap = null;
            Size = new();
        }

        public bool Draw(PaintEventArgs _) {
            try {
                _renderTarget.BeginDraw();
                _renderTarget.FillRectangle(_control.ClientRectangle.ToSilkFloat(), _backColorBrush);
                if (!HasImage) {
                    Marshal.ThrowExceptionForHR(_renderTarget.EndDraw(new Span<ulong>(), new Span<ulong>()));
                    return true;
                }

                var imageRect = _control.Viewport.EffectiveRect;
                var insetRect = new Rectangle(
                    _control.Padding.Left,
                    _control.Padding.Top,
                    _control.Width - _control.Padding.Left - _control.Padding.Right,
                    _control.Height - _control.Padding.Bottom - _control.Padding.Top);

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
                    new Box2D<float>(0, 0, Size.Width, Size.Height));

                _renderTarget.DrawRectangle(
                    Rectangle.Inflate(imageRect, 1, 1).ToSilkFloat(),
                    _borderColorBrush,
                    1f, // stroke width
                    new ComPtr<ID2D1StrokeStyle>());

                // TODO: draw text information

                Marshal.ThrowExceptionForHR(_renderTarget.EndDraw(new Span<ulong>(), new Span<ulong>()));
                return true;
            } catch (Exception e) {
                LastException = e;
                return false;
            }
        }

        // Ignore errors, as it'll be thrown again on EndDraw
        private void ControlOnResize(object? sender, EventArgs e) {
            _renderTarget.Resize(new Vector2D<uint>((uint) _control.Width, (uint) _control.Height));
            _control.Invalidate();
        }
    }
}
