using System;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using AlphaMode = Silk.NET.Direct2D.AlphaMode;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;
using IDWriteTextLayout = Silk.NET.DirectWrite.IDWriteTextLayout;

namespace LuminaExplorer.Controls.DirectXStuff;

public abstract unsafe class D2DRenderer<T> : DirectXObject where T : Control {
    private readonly object _renderTargetObtainLock = new();

    private IDXGISwapChain* _pDxgiSwapChain;
    private IDXGISurface* _pDxgiSurface;
    private ID2D1RenderTarget* _pRenderTarget2D;
    private ID3D11RenderTargetView* _pRenderTarget3D;

    private ID2D1Brush* _pForeColorBrush;
    private ID2D1Brush* _pBackColorBrush;
    private IDWriteTextFormat* _pFontTextFormat;

    private nint _controlHandle;

    protected D2DRenderer(T control, ID3D11Device* pDevice = null, ID3D11DeviceContext* pDeviceContext = null) {
        Control = control;
        try {
            TryInitializeApis();
            Device = pDevice is not null ? pDevice : SharedD3D11Device;
            DeviceContext = pDeviceContext is not null ? pDeviceContext : SharedD3D11DeviceContext;
        } catch (Exception e) {
            LastException = e;
        }
    }

    public void UiThreadInitialize() {
        try {
            _controlHandle = Control.Handle;
            Control.ClientSizeChanged += ControlOnClientSizeChanged;
            Control.ForeColorChanged += ControlOnForeColorChanged;
            Control.BackColorChanged += ControlOnBackColorChanged;
            Control.FontChanged += ControlOnFontChanged;
        } catch (Exception e) {
            LastException = e;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Control.ClientSizeChanged -= ControlOnClientSizeChanged;
            Control.ForeColorChanged -= ControlOnForeColorChanged;
            Control.BackColorChanged -= ControlOnBackColorChanged;
            Control.FontChanged -= ControlOnFontChanged;
        }

        SafeRelease(ref _pForeColorBrush);
        SafeRelease(ref _pBackColorBrush);
        SafeRelease(ref _pDxgiSwapChain);
        SafeRelease(ref _pRenderTarget2D);
        SafeRelease(ref _pRenderTarget3D);
        SafeRelease(ref _pDxgiSurface);

        base.Dispose(disposing);
    }

    public T Control { get; }

    public ID3D11Device* Device { get; }

    public ID3D11DeviceContext* DeviceContext { get; }

    public Exception? LastException { get; protected set; }

    protected ID2D1Brush* ForeColorBrush => GetOrCreateSolidColorBrush(ref _pForeColorBrush, Control.ForeColor);

    protected ID2D1Brush* BackColorBrush => GetOrCreateSolidColorBrush(ref _pBackColorBrush, Control.BackColor);

    protected IDWriteTextFormat* FontTextFormat => GetOrCreateFromFont(ref _pFontTextFormat, Control.Font);

    protected ID2D1RenderTarget* RenderTarget2D {
        get {
            if (_pRenderTarget2D is not null)
                return _pRenderTarget2D;
            
            SetUpRenderTargets();
            return _pRenderTarget2D;
            
        }
    }

    protected ID3D11RenderTargetView* RenderTarget3D {
        get {
            if (_pRenderTarget3D is not null)
                return _pRenderTarget3D;
            
            SetUpRenderTargets();
            return _pRenderTarget3D;
            
        }
    }

    private void SetUpRenderTargets() {
        lock (_renderTargetObtainLock) {
            if (_pRenderTarget2D is not null && _pRenderTarget3D is not null)
                return;

            SafeRelease(ref _pDxgiSurface);
            SafeRelease(ref _pRenderTarget2D);
            SafeRelease(ref _pRenderTarget3D);

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
                        BufferCount = 2,
                        BufferUsage = DXGI.UsageRenderTargetOutput,
                        SwapEffect = SwapEffect.FlipSequential,
                        OutputWindow = _controlHandle,
                        Windowed = true,
                    };

                    SafeRelease(ref _pDxgiSwapChain);
                    fixed (IDXGISwapChain** ppSwapChain = &_pDxgiSwapChain)
                        ThrowH(DxgiFactory->CreateSwapChain((IUnknown*) Device, &desc, ppSwapChain));
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

                fixed (ID2D1RenderTarget** ppRenderTarget = &_pRenderTarget2D)
                    ThrowH(D2DFactory->CreateDxgiSurfaceRenderTarget(
                        _pDxgiSurface, &rtp, ppRenderTarget));

                fixed (ID3D11RenderTargetView** ppRenderTarget = &_pRenderTarget3D) {
                    using var qi = _pDxgiSurface->QueryInterface<ID3D11Resource>();
                    ThrowH(Device->CreateRenderTargetView(qi.Handle, null, ppRenderTarget));
                }
            } catch (Exception e) {
                throw LastException = e;
            }
        }
    }
    
    private void ControlOnForeColorChanged(object? sender, EventArgs e) => SafeRelease(ref _pForeColorBrush);

    private void ControlOnBackColorChanged(object? sender, EventArgs e) => SafeRelease(ref _pBackColorBrush);

    private void ControlOnFontChanged(object? sender, EventArgs e) => SafeRelease(ref _pFontTextFormat);

    private void ControlOnClientSizeChanged(object? sender, EventArgs e) {
        SafeRelease(ref _pRenderTarget2D);
        SafeRelease(ref _pRenderTarget3D);
        SafeRelease(ref _pDxgiSurface);
    }

    protected abstract void Draw3D(ID3D11RenderTargetView* pRenderTarget);

    protected abstract void Draw2D(ID2D1RenderTarget* pRenderTarget);

    public virtual bool Draw(PaintEventArgs eventArgs) {
        try {
            if (Control.Width != 0 && Control.Height != 0) {
                var viewport = new Viewport {
                    //TopLeftX = eventArgs.ClipRectangle.X,
                    //TopLeftY = eventArgs.ClipRectangle.Y,
                    //Width = eventArgs.ClipRectangle.Width,
                    //Height = eventArgs.ClipRectangle.Height,
                    TopLeftX = 0,
                    TopLeftY = 0,
                    Width = Control.Width,
                    Height = Control.Height,
                    MinDepth = 0f,
                    MaxDepth = 1f,
                };
                DeviceContext->RSSetViewports(1, viewport);
                DeviceContext->OMSetRenderTargets(1, RenderTarget3D, null);
                Draw3D(RenderTarget3D);

                var pRenderTarget = RenderTarget2D;
                pRenderTarget->BeginDraw();
                var errorPending = false;
                try {
                    Draw2D(pRenderTarget);
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

    protected IDWriteTextLayout* LayoutText(
        out TextMetrics metrics,
        string? @string,
        RectangleF rectangle,
        WordWrapping? wordWrapping = null,
        TextAlignment? textAlignment = null,
        ParagraphAlignment? paragraphAlignment = null,
        IDWriteTextFormat* textFormat = null) {
        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textFormat is null)
            textFormat = FontTextFormat;

        if (wordWrapping is not null)
            textFormat->SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat->SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat->SetParagraphAlignment(paragraphAlignment.Value);

        IDWriteTextLayout* layout = null;
        fixed (char* c = (string.IsNullOrEmpty(@string) ? "\0" : @string).AsSpan())
            ThrowH(DWriteFactory->CreateTextLayout(
                c,
                (uint) (string.IsNullOrEmpty(@string) ? 0 : @string.Length),
                textFormat,
                1f * rectangle.Width,
                1f * rectangle.Height,
                &layout));
        try {
            fixed (TextMetrics* ptm = &metrics) {
                // ThrowH(layout->GetMetrics(ptm));
                ThrowH(((delegate* unmanaged[Stdcall]<IDWriteTextLayout*, TextMetrics*, int>) layout->LpVtbl[60])(
                    layout, ptm));
            }

            var layoutCopy = layout;
            layout = null;
            return layoutCopy;
        } finally {
            SafeRelease(ref layout);
        }
    }

    protected void DrawText(string? @string,
        RectangleF rectangle,
        WordWrapping? wordWrapping = null,
        TextAlignment? textAlignment = null,
        ParagraphAlignment? paragraphAlignment = null,
        IDWriteTextFormat* textFormat = null,
        ID2D1Brush* textBrush = null,
        ID2D1Brush* shadowBrush = null,
        float opacity = 1f,
        int borderWidth = 0) {
        if (opacity <= 0 || string.IsNullOrWhiteSpace(@string))
            return;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textFormat is null)
            textFormat = FontTextFormat;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textBrush is null)
            textBrush = ForeColorBrush;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (shadowBrush is null)
            shadowBrush = BackColorBrush;

        if (wordWrapping is not null)
            textFormat->SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat->SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat->SetParagraphAlignment(paragraphAlignment.Value);

        shadowBrush->SetOpacity(opacity);
        textBrush->SetOpacity(opacity);

        var pRenderTarget = RenderTarget2D;

        var box = rectangle.ToSilkValue();
        fixed (char* pString = @string.AsSpan()) {
            for (var i = -borderWidth; i <= borderWidth; i++) {
                for (var j = -borderWidth; j <= borderWidth; j++) {
                    if (i == 0 && j == 0)
                        continue;
                    box = (rectangle with {X = rectangle.X + i, Y = rectangle.Y + j}).ToSilkValue();
                    pRenderTarget->DrawTextA(
                        pString,
                        (uint) @string.Length,
                        (Silk.NET.Direct2D.IDWriteTextFormat*) textFormat,
                        &box,
                        shadowBrush,
                        DrawTextOptions.None,
                        DwriteMeasuringMode.GdiNatural);
                }
            }

            box = rectangle.ToSilkValue();
            pRenderTarget->DrawTextA(
                pString,
                (uint) @string.Length,
                (Silk.NET.Direct2D.IDWriteTextFormat*) textFormat,
                &box,
                textBrush,
                DrawTextOptions.None,
                DwriteMeasuringMode.GdiNatural);
        }
    }

    protected ID2D1Brush* CreateSolidColorBrush(Color color) {
        ID2D1Brush* pBrush = null;
        ThrowH(RenderTarget2D->CreateSolidColorBrush(
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

    protected ID2D1Bitmap* CreateFromWicBitmap(WicNet.WicBitmapSource? wicBitmapSource) {
        ID2D1Bitmap* pBitmap = null;
        if (wicBitmapSource is null)
            pBitmap = null;
        else
            ThrowH(RenderTarget2D->CreateBitmapFromWicBitmap(
                (IWICBitmapSource*) wicBitmapSource.ComObject.GetInterfacePointer<DirectN.IWICBitmapSource>(),
                null,
                &pBitmap));

        return pBitmap;
    }

    protected ID2D1Bitmap* GetOrCreateFromWicBitmap(ref ID2D1Bitmap* pBitmap, WicNet.WicBitmapSource? wicBitmapSource) {
        if (pBitmap is null)
            pBitmap = CreateFromWicBitmap(wicBitmapSource);
        return pBitmap;
    }

    protected IDWriteTextFormat* GetOrCreateFromFont(ref IDWriteTextFormat* textFormat, Font font) {
        if (textFormat is null)
            fixed (char* pName = font.Name.AsSpan())
            fixed (char* pEmpty = "\0".AsSpan())
            fixed (IDWriteTextFormat** ppFontTextFormat = &textFormat)
                ThrowH(DWriteFactory->CreateTextFormat(
                    pName,
                    null,
                    font.Bold ? FontWeight.Bold : FontWeight.Normal,
                    font.Italic ? FontStyle.Italic : FontStyle.Normal,
                    FontStretch.Normal,
                    font.SizeInPoints * 4 / 3,
                    pEmpty,
                    ppFontTextFormat));
        return textFormat;
    }
}
