using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using AlphaMode = Silk.NET.Direct2D.AlphaMode;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;

namespace LuminaExplorer.Controls.Util;

public abstract unsafe class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    protected readonly T Control;

    private IDXGISwapChain* _pDxgiSwapChain;
    private ID3D11DeviceContext* _pD3dContext;
    private IDXGISurface* _pDxgiSurface;
    private ID2D1RenderTarget* _pRenderTarget;

    private ID2D1Brush* _pForeColorBrush;
    private Color _foreColor;

    private ID2D1Brush* _pBackColorBrush;
    private Color _backColor;

    private IDWriteTextFormat* _pFontTextFormat;
    private Font _font = null!;

    protected BaseD2DRenderer(T control) {
        Control = control;

        try {
            TryInitializeApis();

            Control.Resize += ControlOnResize;

            ForeColor = Control.ForeColor;
            BackColor = Control.BackColor;
            Font = Control.Font;
        } catch (Exception e) {
            LastException = e;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            Control.Resize -= ControlOnResize;

        SafeRelease(ref _pForeColorBrush);
        SafeRelease(ref _pBackColorBrush);
        SafeRelease(ref _pDxgiSwapChain);
        SafeRelease(ref _pD3dContext);
        SafeRelease(ref _pRenderTarget);
        SafeRelease(ref _pDxgiSurface);
    }

    public Exception? LastException { get; protected set; }

    public Color ForeColor {
        get => _foreColor;
        set {
            if (_foreColor == value)
                return;
            _foreColor = value;
            SafeRelease(ref _pForeColorBrush);
        }
    }

    protected ID2D1Brush* ForeColorBrush => GetOrCreateSolidColorBrush(ref _pForeColorBrush, ForeColor);

    public Color BackColor {
        get => _backColor;
        set {
            if (_backColor == value)
                return;
            _backColor = value;
            SafeRelease(ref _pBackColorBrush);
        }
    }

    protected ID2D1Brush* BackColorBrush => GetOrCreateSolidColorBrush(ref _pBackColorBrush, BackColor);

    public Font Font {
        get => _font;
        set {
            if (Equals(_font, value))
                return;
            _font = value;
            SafeRelease(ref _pFontTextFormat);
        }
    }

    protected IDWriteTextFormat* FontTextFormat => GetOrCreateFromFont(ref _pFontTextFormat, Font);

    protected ID2D1RenderTarget* RenderTarget {
        get {
            if (_pRenderTarget is not null)
                return _pRenderTarget;

            SafeRelease(ref _pDxgiSurface);

            try {
                if (_pDxgiSwapChain is null) {
                    var desc = new SwapChainDesc {
                        BufferDesc = new() {
                            Width = 0,
                            Height = 0,
                            Format = Format.FormatB8G8R8A8Unorm,
                            RefreshRate = new(1, 60),
                            Scaling = ModeScaling.Centered,
                        },
                        SampleDesc = new() {
                            Count = 1,
                            Quality = 0,
                        },
                        BufferCount = 1,
                        BufferUsage = DXGI.UsageRenderTargetOutput,
                        OutputWindow = Control.Handle,
                        Windowed = true,
                    };

                    SafeRelease(ref _pDxgiSwapChain);
                    SafeRelease(ref _pD3dContext);
                    fixed (IDXGISwapChain** ppSwapChain = &_pDxgiSwapChain)
                        ThrowH(DxgiFactory->CreateSwapChain(
                            (IUnknown*) SharedD3D11Device,
                            &desc,
                            ppSwapChain));
                }

                ThrowH(_pDxgiSwapChain->ResizeBuffers(0, 0, 0, Format.FormatUnknown, 0));

                fixed (void* ppNewSurface = &_pDxgiSurface)
                fixed (Guid* g = &IDXGISurface.Guid)
                    ThrowH(_pDxgiSwapChain->GetBuffer(0, g, (void**) ppNewSurface));

                var rtp = new RenderTargetProperties {
                    Type = RenderTargetType.Default,
                    PixelFormat = new() {
                        AlphaMode = AlphaMode.Premultiplied,
                        Format = Format.FormatUnknown,
                    },
                    DpiX = Control.DeviceDpi,
                    DpiY = Control.DeviceDpi,
                };

                fixed (ID2D1RenderTarget** pRenderTarget = &_pRenderTarget)
                    ThrowH(D2DFactory->CreateDxgiSurfaceRenderTarget(
                        _pDxgiSurface, &rtp, pRenderTarget));

                return _pRenderTarget;
            } catch (Exception e) {
                LastException = e;
                throw;
            }
        }
    }

    protected void ControlOnResize(object? sender, EventArgs e) {
        SafeRelease(ref _pRenderTarget);
        SafeRelease(ref _pDxgiSurface);

        Control.Invalidate();
    }

    protected abstract void DrawInternal();

    public bool Draw(PaintEventArgs _) {
        try {
            if (Control.Width != 0 && Control.Height != 0) {
                var pRenderTarget = RenderTarget;
                pRenderTarget->BeginDraw();
                var errorPending = false;
                try {
                    DrawInternal();
                } catch (Exception) {
                    errorPending = true;
                    throw;
                } finally {
                    var hr = pRenderTarget->EndDraw(null, null);
                    if (!errorPending)
                        ThrowH(hr);
                }

                _pDxgiSwapChain->Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            LastException = e;
            return false;
        }
    }

    protected void DrawContrastingText(
        string @string,
        Rectangle rectangle,
        WordWrapping? wordWrapping = null,
        TextAlignment? textAlignment = null,
        ParagraphAlignment? paragraphAlignment = null,
        float opacity = 1,
        int borderWidth = 2,
        IDWriteTextFormat* pTextFormat = null,
        ID2D1Brush* pForeBrush = null,
        ID2D1Brush* pBackBrush = null) {

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (pTextFormat is null)
            pTextFormat = FontTextFormat;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (pForeBrush is null)
            pForeBrush = ForeColorBrush;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (pBackBrush is null)
            pBackBrush = BackColorBrush;

        if (wordWrapping is not null)
            pTextFormat->SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            pTextFormat->SetTextAlignment(textAlignment.Value);
        
        if (paragraphAlignment is not null)
            pTextFormat->SetParagraphAlignment(paragraphAlignment.Value);

        var disposeBrushAfter = false;
        switch (opacity) {
            case 0:
                return;
            case >= 1:
                break;
            default:
                pBackBrush = CreateSolidColorBrush(Color.FromArgb(
                    (byte) (BackColor.A * opacity),
                    BackColor.R,
                    BackColor.G,
                    BackColor.B));
                pForeBrush = CreateSolidColorBrush(Color.FromArgb(
                    (byte) (ForeColor.A * opacity),
                    ForeColor.R,
                    ForeColor.G,
                    ForeColor.B));
                disposeBrushAfter = true;
                break;
        }

        try {
            var pRenderTarget = RenderTarget;

            var box = rectangle.ToSilkFloat();
            fixed (char* pString = @string.AsSpan()) {
                for (var i = -borderWidth; i <= borderWidth; i++) {
                    for (var j = -borderWidth; j <= borderWidth; j++) {
                        if (i == 0 && j == 0)
                            continue;
                        box = (rectangle with {X = rectangle.X + i, Y = rectangle.Y + j}).ToSilkFloat();
                        pRenderTarget->DrawTextA(
                            pString,
                            (uint) @string.Length,
                            (Silk.NET.Direct2D.IDWriteTextFormat*) pTextFormat,
                            &box,
                            pBackBrush,
                            DrawTextOptions.None,
                            DwriteMeasuringMode.GdiNatural);
                    }
                }

                box = rectangle.ToSilkFloat();
                pRenderTarget->DrawTextA(
                    pString,
                    (uint) @string.Length,
                    (Silk.NET.Direct2D.IDWriteTextFormat*) pTextFormat,
                    &box,
                    pForeBrush,
                    DrawTextOptions.None,
                    DwriteMeasuringMode.GdiNatural);
            }
        } finally {
            if (disposeBrushAfter) {
                SafeRelease(ref pBackBrush);
                SafeRelease(ref pForeBrush);
            }
                
        }
    }

    protected ID2D1Brush* CreateSolidColorBrush(Color color) {
        ID2D1Brush* pBrush = null;
        ThrowH(RenderTarget->CreateSolidColorBrush(
            new D3Dcolorvalue(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f),
            null,
            (ID2D1SolidColorBrush**) &pBrush));
        return pBrush;
    }

    protected ID2D1Brush* GetOrCreateSolidColorBrush(ref ID2D1Brush* pBrush, Color color) {
        if (pBrush is null)
            pBrush = CreateSolidColorBrush(color);
        return pBrush;
    }

    protected ID2D1Bitmap* GetOrCreateFromWicBitmap(ref ID2D1Bitmap* pBitmap, WicNet.WicBitmapSource? wicBitmapSource) {
        if (wicBitmapSource is null)
            pBitmap = null;
        else if (pBitmap is null)
            fixed (ID2D1Bitmap** ppBitmap = &pBitmap)
                ThrowH(RenderTarget->CreateBitmapFromWicBitmap(
                    (IWICBitmapSource*) wicBitmapSource.ComObject.GetInterfacePointer<DirectN.IWICBitmapSource>(),
                    null,
                    ppBitmap));
        return pBitmap;
    }

    protected IDWriteTextFormat* GetOrCreateFromFont(ref IDWriteTextFormat* pTextLayout, Font font) {
        if (pTextLayout is null)
            fixed (char* pName = font.Name.AsSpan())
            fixed (char* pEmpty = "\0".AsSpan())
            fixed (IDWriteTextFormat** ppFontTextFormat = &pTextLayout)
                ThrowH(DWriteFactory->CreateTextFormat(
                    pName,
                    null,
                    font.Bold ? FontWeight.Bold : FontWeight.Normal,
                    font.Italic ? FontStyle.Italic : FontStyle.Normal,
                    FontStretch.Normal,
                    font.SizeInPoints * 4 / 3,
                    pEmpty,
                    ppFontTextFormat));
        return pTextLayout;
    }
}
