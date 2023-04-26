using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using DirectN;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.Util;

public abstract class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    private readonly object _renderTargetObtainLock = new();
    private Thread _mainThread = null!;

    private IComObject<IDXGISwapChain>? _dxgiSwapChain;
    private IComObject<ID3D11DeviceContext>? _d3dContext;
    private IComObject<IDXGISurface>? _dxgiSurface;
    private IComObject<ID2D1RenderTarget>? _renderTarget;
    private IComObject<ID2D1Brush>? _foreColorBrush;
    private IComObject<ID2D1Brush>? _backColorBrush;
    private IComObject<IDWriteTextFormat>? _fontTextFormat;

    private nint _controlHandle;

    protected BaseD2DRenderer(T control) {
        Control = control;
        try {
            TryInitializeApis();
        } catch (Exception e) {
            LastException = e;
        }
    }

    public void UiThreadInitialize() {
        try {
            _mainThread = Thread.CurrentThread;
            _controlHandle = Control.Handle;
            Control.Resize += ControlOnResize;
            Control.ForeColorChanged += ControlOnForeColorChanged;
            Control.BackColorChanged += ControlOnBackColorChanged;
            Control.FontChanged += ControlOnFontChanged;
        } catch (Exception e) {
            LastException = e;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Control.Resize -= ControlOnResize;
            Control.ForeColorChanged -= ControlOnForeColorChanged;
            Control.BackColorChanged -= ControlOnBackColorChanged;
            Control.FontChanged -= ControlOnFontChanged;
        }

        SafeDispose.One(ref _foreColorBrush);
        SafeDispose.One(ref _backColorBrush);
        SafeDispose.One(ref _dxgiSwapChain);
        SafeDispose.One(ref _d3dContext);
        SafeDispose.One(ref _renderTarget);
        SafeDispose.One(ref _dxgiSurface);

        base.Dispose(disposing);
    }

    public T Control { get; }

    public Exception? LastException { get; protected set; }

    protected IComObject<ID2D1Brush> ForeColorBrush =>
        GetOrCreateSolidColorBrush(ref _foreColorBrush, Control.ForeColor);

    protected IComObject<ID2D1Brush> BackColorBrush =>
        GetOrCreateSolidColorBrush(ref _backColorBrush, Control.BackColor);

    protected IComObject<IDWriteTextFormat> FontTextFormat => GetOrCreateFromFont(ref _fontTextFormat, Control.Font);

    protected IComObject<ID2D1RenderTarget> RenderTarget {
        get {
            if (_renderTarget is not null)
                return _renderTarget;

            lock (_renderTargetObtainLock) {
                if (_renderTarget is not null)
                    return _renderTarget;

                SafeDispose.One(ref _dxgiSurface);

                (Exception? exc, object? unused) result = new();

                void DoObtain() {
                    try {
                        if (_dxgiSwapChain is null) {
                            var desc = new DXGI_SWAP_CHAIN_DESC {
                                BufferDesc = new() {
                                    Width = 0,
                                    Height = 0,
                                    Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                                    RefreshRate = new() {Numerator = 1, Denominator = 60},
                                },
                                SampleDesc = new() {
                                    Count = 1,
                                    Quality = 0,
                                },
                                BufferCount = 1,
                                BufferUsage = 1 << 5, // DXGI_USAGE_RENDER_TARGET_OUTPUT
                                OutputWindow = _controlHandle,
                                Windowed = true,
                            };

                            SafeDispose.One(ref _dxgiSwapChain);
                            SafeDispose.One(ref _d3dContext);
                            DxgiFactory.CreateSwapChain(SharedD3D11Device, ref desc, out var swapChain)
                                .ThrowOnError();
                            _dxgiSwapChain = new ComObject<IDXGISwapChain>(swapChain);
                        }

                        _dxgiSwapChain.Object.ResizeBuffers(0, 0, 0, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0)
                            .ThrowOnError();

                        _dxgiSurface = _dxgiSwapChain.Object.GetBuffer<IDXGISurface>(0);

                        var rtp = new D2D1_RENDER_TARGET_PROPERTIES {
                            type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
                            pixelFormat = new() {
                                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                                format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                            },
                            dpiX = Control.DeviceDpi,
                            dpiY = Control.DeviceDpi,
                        };

                        D2DFactory.CreateDxgiSurfaceRenderTarget(_dxgiSurface.Object, ref rtp, out var rt)
                            .ThrowOnError();
                        _renderTarget = new ComObject<ID2D1RenderTarget>(rt);
                    } catch (Exception e) {
                        result.exc = e;
                        throw;
                    }
                }

                if (_mainThread == Thread.CurrentThread)
                    DoObtain();
                else {
                    using var wh = Control.BeginInvoke(DoObtain).AsyncWaitHandle;
                    wh.WaitOne();
                }

                if (result.exc is not null)
                    throw LastException = result.exc;

                Debug.Assert(_renderTarget is not null);

                return _renderTarget;
            }
        }
    }

    private void ControlOnForeColorChanged(object? sender, EventArgs e) => SafeDispose.One(ref _foreColorBrush);

    private void ControlOnBackColorChanged(object? sender, EventArgs e) => SafeDispose.One(ref _backColorBrush);

    private void ControlOnFontChanged(object? sender, EventArgs e) => SafeDispose.One(ref _fontTextFormat);

    private void ControlOnResize(object? sender, EventArgs e) {
        SafeDispose.One(ref _renderTarget);
        SafeDispose.One(ref _dxgiSurface);
    }

    protected abstract void DrawInternal();

    public virtual bool Draw(PaintEventArgs _) {
        try {
            if (Control.Width != 0 && Control.Height != 0) {
                var pRenderTarget = RenderTarget;
                pRenderTarget.BeginDraw();
                var errorPending = false;
                try {
                    DrawInternal();
                } catch (Exception) {
                    errorPending = true;
                    throw;
                } finally {
                    var hr = pRenderTarget.Object.EndDraw(0, 0);
                    if (!errorPending)
                        hr.ThrowOnError();
                }

                _dxgiSwapChain.Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            LastException = e;
            return false;
        }
    }

    protected IComObject<IDWriteTextLayout> LayoutText(
        out DWRITE_TEXT_METRICS metrics,
        string? @string,
        RectangleF rectangle,
        DWRITE_WORD_WRAPPING? wordWrapping = null,
        DWRITE_TEXT_ALIGNMENT? textAlignment = null,
        DWRITE_PARAGRAPH_ALIGNMENT? paragraphAlignment = null,
        IComObject<IDWriteTextFormat>? textFormat = null) {
        var stringLength = (uint) (@string?.Length ?? 0);
        @string = string.IsNullOrEmpty(@string) ? "\0" : @string;

        textFormat ??= FontTextFormat;

        if (wordWrapping is not null)
            textFormat.Object.SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat.Object.SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat.Object.SetParagraphAlignment(paragraphAlignment.Value);

        DWriteFactory.CreateTextLayout(
            @string,
            stringLength,
            textFormat.Object,
            1f * rectangle.Width,
            1f * rectangle.Height,
            out var layoutPtr).ThrowOnError();
        var layout = new ComObject<IDWriteTextLayout>(layoutPtr);
        try {
            layout.Object.GetMetrics(out metrics);

            var layoutCopy = layout;
            layout = null;
            return layoutCopy;
        } finally {
            SafeDispose.One(ref layout);
        }
    }

    protected void DrawText(string? @string,
        Rectangle rectangle,
        DWRITE_WORD_WRAPPING? wordWrapping = null,
        DWRITE_TEXT_ALIGNMENT? textAlignment = null,
        DWRITE_PARAGRAPH_ALIGNMENT? paragraphAlignment = null,
        IComObject<IDWriteTextFormat>? textFormat = null,
        IComObject<ID2D1Brush>? textBrush = null,
        IComObject<ID2D1Brush>? shadowBrush = null,
        float opacity = 1f,
        int borderWidth = 0) {
        if (opacity <= 0 || string.IsNullOrWhiteSpace(@string))
            return;

        textFormat ??= FontTextFormat;

        textBrush ??= ForeColorBrush;
        textBrush.Object.SetOpacity(opacity);

        if (borderWidth > 0) {
            shadowBrush ??= BackColorBrush;
            shadowBrush.Object.SetOpacity(opacity);
        }

        if (wordWrapping is not null)
            textFormat.Object.SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat.Object.SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat.Object.SetParagraphAlignment(paragraphAlignment.Value);

        var renderTarget = RenderTarget;

        for (var i = -borderWidth; i <= borderWidth; i++) {
            for (var j = -borderWidth; j <= borderWidth; j++) {
                if (i == 0 && j == 0)
                    continue;
                renderTarget.DrawText(
                    @string,
                    textFormat,
                    (rectangle with {X = rectangle.X + i, Y = rectangle.Y + j}).ToSilkFloat(),
                    textBrush,
                    D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_GDI_NATURAL);
            }
        }

        renderTarget.DrawText(
            @string,
            textFormat,
            rectangle.ToSilkFloat(),
            textBrush,
            D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE,
            DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_GDI_NATURAL);
    }

    protected IComObject<ID2D1Brush> CreateSolidColorBrush(Color color) =>
        RenderTarget.Object.CreateSolidColorBrush<ID2D1SolidColorBrush>(
            new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));

    protected IComObject<ID2D1Brush> GetOrCreateSolidColorBrush(ref IComObject<ID2D1Brush>? pBrush, Color color) => 
        pBrush ??= CreateSolidColorBrush(color);

    protected IComObject<ID2D1Bitmap> CreateFromWicBitmap(IComObject<IWICBitmapSource> wicBitmapSource) {
        ID2D1Bitmap o;
        
        if (wicBitmapSource.ConvertPixelFormatDifferent(
                out var after,
                WICConstants.GUID_WICPixelFormat32bppPBGRA,
                false)) {
            using (after) {
                RenderTarget.Object.CreateBitmapFromWicBitmap(after.Object, 0, out o).ThrowOnError();
                return new ComObject<ID2D1Bitmap>(o);
            }
        }

        RenderTarget.Object.CreateBitmapFromWicBitmap(wicBitmapSource.Object, 0, out o).ThrowOnError();
        return new ComObject<ID2D1Bitmap>(o);
    }

    protected IComObject<ID2D1Bitmap> GetOrCreateFromWicBitmap(
        ref IComObject<ID2D1Bitmap>? pBitmap,
        IComObject<IWICBitmapSource> wicBitmapSource) =>
        pBitmap ??= CreateFromWicBitmap(wicBitmapSource);

    protected IComObject<IDWriteTextFormat> GetOrCreateFromFont(
        ref IComObject<IDWriteTextFormat>? textFormat,
        Font font) {
        if (textFormat is not null)
            return textFormat;
            
        DWriteFactory.CreateTextFormat(
            Control.Font.FontFamily.Name,
            null,
            Control.Font.Bold
                ? DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD
                : DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
            Control.Font.Italic
                ? DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC
                : DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            Control.Font.SizeInPoints * 4 / 3,
            "",
            out var format).ThrowOnError();
        return textFormat = new ComObject<IDWriteTextFormat>(format);
    }
}
