using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using AlphaMode = Silk.NET.Direct2D.AlphaMode;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;
using IDWriteTextLayout = Silk.NET.DirectWrite.IDWriteTextLayout;

namespace LuminaExplorer.Controls.Util;

public abstract unsafe class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    private IDXGISwapChain* _pDxgiSwapChain;
    private ID3D11DeviceContext* _pD3dContext;
    private IDXGISurface* _pDxgiSurface;
    private ID2D1RenderTarget* _pRenderTarget;

    private ID2D1Brush* _pForeColorBrush;
    private ID2D1Brush* _pBackColorBrush;
    private IDWriteTextFormat* _pFontTextFormat;

    protected BaseD2DRenderer(T control) {
        Control = control;

        try {
            TryInitializeApis();

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

        SafeRelease(ref _pForeColorBrush);
        SafeRelease(ref _pBackColorBrush);
        SafeRelease(ref _pDxgiSwapChain);
        SafeRelease(ref _pD3dContext);
        SafeRelease(ref _pRenderTarget);
        SafeRelease(ref _pDxgiSurface);
        
        base.Dispose(disposing);
    }

    public T Control { get; }

    public Exception? LastException { get; protected set; }

    protected ID2D1Brush* ForeColorBrush => GetOrCreateSolidColorBrush(ref _pForeColorBrush, Control.ForeColor);

    protected ID2D1Brush* BackColorBrush => GetOrCreateSolidColorBrush(ref _pBackColorBrush, Control.BackColor);

    protected IDWriteTextFormat* FontTextFormat => GetOrCreateFromFont(ref _pFontTextFormat, Control.Font);

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

    private void ControlOnForeColorChanged(object? sender, EventArgs e) => SafeRelease(ref _pForeColorBrush);

    private void ControlOnBackColorChanged(object? sender, EventArgs e) => SafeRelease(ref _pBackColorBrush);

    private void ControlOnFontChanged(object? sender, EventArgs e) => SafeRelease(ref _pFontTextFormat);

    private void ControlOnResize(object? sender, EventArgs e) {
        SafeRelease(ref _pRenderTarget);
        SafeRelease(ref _pDxgiSurface);
    }

    protected abstract void DrawInternal();

    public virtual bool Draw(PaintEventArgs _) {
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

    protected IDWriteTextLayout* LayoutText(
        out TextMetrics metrics,
        string? @string,
        Rectangle rectangle,
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
                ThrowH(((delegate* unmanaged[Stdcall]<IDWriteTextLayout*, TextMetrics*, int>) layout->LpVtbl[60])(layout, ptm));
            }

            var layoutCopy = layout;
            layout = null;
            return layoutCopy;
        } finally {
            SafeRelease(ref layout);
        }
    }

    protected void DrawText(string? @string,
        Rectangle rectangle,
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
                        (Silk.NET.Direct2D.IDWriteTextFormat*) textFormat,
                        &box,
                        shadowBrush,
                        DrawTextOptions.None,
                        DwriteMeasuringMode.GdiNatural);
                }
            }

            box = rectangle.ToSilkFloat();
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
                try {
                    ThrowH(RenderTarget->CreateBitmapFromWicBitmap(
                        (IWICBitmapSource*) wicBitmapSource.ComObject.GetInterfacePointer<DirectN.IWICBitmapSource>(),
                        null,
                        ppBitmap));
                } catch (Exception e) {
                    Debugger.Break();
                }

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
