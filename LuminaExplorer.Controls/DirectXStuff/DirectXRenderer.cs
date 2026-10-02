using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff;

public abstract unsafe class DirectXRenderer<T> : DirectXObject where T : Control {
    private readonly object _renderTargetObtainLock = new();

    private ComPtr<IDXGISwapChain> _pDxgiSwapChain;
    private ComPtr<IDXGISurface> _pDxgiSurface;
    private ComPtr<ID2D1RenderTarget> _pRenderTarget2D;
    private ComPtr<ID3D11RenderTargetView> _pRenderTarget3D;

    private ComPtr<ID2D1Brush> _pForeColorBrush;
    private ComPtr<ID2D1Brush> _pBackColorBrush;
    private ComPtr<IDWriteTextFormat> _pFontTextFormat;

    private readonly bool _useDepthStencil;
    private DepthStencilResource? _depthStencilResource;
    private ComPtr<ID3D11BlendState> _pBlendState;
    private ComPtr<ID3D11RasterizerState> _pRasterizerState;

    private HWND _controlHandle;

    private CancellationTokenSource? _autoInvalidateCancellationTokenSource;
    private Thread? _autoInvalidateThread;

    protected DirectXRenderer(
        T control,
        bool useDepthStencil,
        ID3D11Device* pDevice = null,
        ID3D11DeviceContext* pDeviceContext = null)
    {
        this.Control = control;
        try {
            TryInitializeApis();
            this.Device = pDevice is not null ? pDevice : SharedD3D11Device;
            this.DeviceContext = pDeviceContext is not null ? pDeviceContext : SharedD3D11DeviceContext;
            this._useDepthStencil = useDepthStencil;

            // Below: entirely copied from SaintCoinach
            var blendDesc = new D3D11_BLEND_DESC {
                RenderTarget = {
                    e0 = {
                        BlendEnable = false,
                        SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE,
                        DestBlend = D3D11_BLEND.D3D11_BLEND_ZERO,
                        BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
                        SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
                        DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO,
                        BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
                        RenderTargetWriteMask = (byte) D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
                    },
                },
            };
            fixed (ID3D11BlendState** ppBlendState = &this._pBlendState.GetPinnableReference())
                this.Device->CreateBlendState(&blendDesc, ppBlendState).Ensure();

            // Below: default values from SharpDX, except where noted
            var rasterizerDesc = new D3D11_RASTERIZER_DESC(
                fillMode: D3D11_FILL_MODE.D3D11_FILL_SOLID,
                cullMode: D3D11_CULL_MODE.D3D11_CULL_FRONT, // SaintCoinach
                frontCounterClockwise: false,
                depthBias: 0,
                slopeScaledDepthBias: 0,
                depthBiasClamp: 0,
                depthClipEnable: true,
                scissorEnable: false,
                multisampleEnable: true, // SaintCoinach
                antialiasedLineEnable: false);
            fixed (ID3D11RasterizerState** ppRasterizerState = &this._pRasterizerState.GetPinnableReference())
                this.Device->CreateRasterizerState(&rasterizerDesc, ppRasterizerState).Ensure();
        } catch (Exception e) {
            this.LastException = e;
        }
    }

    public void UiThreadInitialize()
    {
        try {
            this._controlHandle = (HWND) this.Control.Handle;
            this.Control.ClientSizeChanged += this.ControlOnClientSizeChanged;
            this.Control.ForeColorChanged += this.ControlOnForeColorChanged;
            this.Control.BackColorChanged += this.ControlOnBackColorChanged;
            this.Control.FontChanged += this.ControlOnFontChanged;
            this.Control.DpiChangedAfterParent += this.ControlOnFontChanged;
        } catch (Exception e) {
            this.LastException = e;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._autoInvalidateCancellationTokenSource?.Cancel();
            this.Control.ClientSizeChanged -= this.ControlOnClientSizeChanged;
            this.Control.ForeColorChanged -= this.ControlOnForeColorChanged;
            this.Control.BackColorChanged -= this.ControlOnBackColorChanged;
            this.Control.FontChanged -= this.ControlOnFontChanged;
            this.Control.DpiChangedAfterParent -= this.ControlOnFontChanged;
            SafeDispose.One(ref this._depthStencilResource);
        }

        this._pForeColorBrush.Reset();
        this._pBackColorBrush.Reset();
        this._pFontTextFormat.Reset();
        this._pDxgiSwapChain.Reset();
        this._pRenderTarget2D.Reset();
        this._pRenderTarget3D.Reset();
        this._pDxgiSurface.Reset();
        this._pBlendState.Reset();
        this._pRasterizerState.Reset();

        base.Dispose(disposing);
    }

    public T Control { get; }

    public ID3D11Device* Device { get; }

    public ID3D11DeviceContext* DeviceContext { get; }

    public Exception? LastException { get; protected set; }

    protected bool AutoInvalidate {
        get => this._autoInvalidateThread != null;
        set {
            if (value == (this._autoInvalidateThread != null))
                return;

            this._autoInvalidateCancellationTokenSource?.Cancel();
            this._autoInvalidateCancellationTokenSource = null;
            this._autoInvalidateThread = null;
            if (!value)
                return;

            var cts = this._autoInvalidateCancellationTokenSource = new();
            this._autoInvalidateThread = new(
                () => {
                    IDXGIOutput* pOutput = null;
                    try {
                        this._pDxgiSwapChain.Get()->GetContainingOutput(&pOutput).Ensure();
                        while (!cts.IsCancellationRequested) {
                            pOutput->WaitForVBlank();
                            this.Control.Invalidate();
                        }
                    } catch (Exception) {
                        // swallow
                    } finally {
                        SafeRelease(ref pOutput);
                    }
                }) {
                IsBackground = true,
            };
            this._autoInvalidateThread.Start();
        }
    }

    protected ID2D1Brush* ForeColorBrush =>
        this.GetOrCreateSolidColorBrush(ref this._pForeColorBrush, this.Control.ForeColor);

    protected ID2D1Brush* BackColorBrush =>
        this.GetOrCreateSolidColorBrush(ref this._pBackColorBrush, this.Control.BackColor);

    protected IDWriteTextFormat* FontTextFormat =>
        this.GetOrCreateFromFont(ref this._pFontTextFormat, this.Control.Font);

    protected ID3D11DepthStencilView* DepthStencilView {
        get {
            if (!this._useDepthStencil)
                return null;
            if (this._depthStencilResource is not null)
                return this._depthStencilResource.View;

            try {
                _ = this.RenderTarget2D;
                this._depthStencilResource = new(this.Device, (IUnknown*) this._pDxgiSurface.Get());
                return this._depthStencilResource.View;
            } catch (Exception e) {
                this.LastException = e;
                throw;
            }
        }
    }

    protected ID2D1RenderTarget* RenderTarget2D {
        get {
            if (!this._pRenderTarget2D.IsEmpty())
                return this._pRenderTarget2D;

            this.SetUpRenderTargets();
            return this._pRenderTarget2D;
        }
    }

    protected ID3D11RenderTargetView* RenderTarget3D {
        get {
            if (!this._pRenderTarget3D.IsEmpty())
                return this._pRenderTarget3D;

            this.SetUpRenderTargets();
            return this._pRenderTarget3D;
        }
    }

    private void SetUpRenderTargets()
    {
        lock (this._renderTargetObtainLock) {
            if (!this._pRenderTarget2D.IsEmpty() && !this._pRenderTarget3D.IsEmpty())
                return;

            this._pRenderTarget2D.Reset();
            this._pRenderTarget3D.Reset();
            SafeDispose.One(ref this._depthStencilResource);
            this._pDxgiSurface.Reset();

            try {
                if (this._pDxgiSwapChain.IsEmpty()) {
                    var desc = new DXGI_SWAP_CHAIN_DESC {
                        BufferDesc = new() {
                            Width = 0,
                            Height = 0,
                            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                            RefreshRate = new(1, 60),
                            Scaling = DXGI_MODE_SCALING.DXGI_MODE_SCALING_CENTERED,
                        },
                        SampleDesc = new() {
                            Count = 1,
                            Quality = 0,
                        },
                        BufferCount = 2,
                        BufferUsage = DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                        SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_SEQUENTIAL,
                        OutputWindow = this._controlHandle,
                        Windowed = true,
                    };

                    this._pDxgiSwapChain.Reset();
                    fixed (IDXGISwapChain** ppSwapChain = &this._pDxgiSwapChain.GetPinnableReference())
                        DxgiFactory->CreateSwapChain((IUnknown*) this.Device, &desc, ppSwapChain).Ensure();
                }

                this._pDxgiSwapChain.Get()->ResizeBuffers(0, 0, 0, DXGI_FORMAT.DXGI_FORMAT_UNKNOWN, 0).Ensure();

                fixed (void* ppNewSurface = &this._pDxgiSurface)
                fixed (Guid* g = &IID.IID_IDXGISurface)
                    this._pDxgiSwapChain.Get()->GetBuffer(0, g, (void**) ppNewSurface).Ensure();

                var rtp = new D2D1_RENDER_TARGET_PROPERTIES {
                    type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_DEFAULT,
                    pixelFormat = new() {
                        alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED,
                        format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                    },
                    // Draw in device pixels, same as WinForms coordinates; DPI scaling is applied explicitly.
                    dpiX = 96,
                    dpiY = 96,
                };

                fixed (ID2D1RenderTarget** ppRenderTarget = &this._pRenderTarget2D.GetPinnableReference())
                    D2DFactory->CreateDxgiSurfaceRenderTarget(this._pDxgiSurface, &rtp, ppRenderTarget).Ensure();

                fixed (ID3D11RenderTargetView** ppRenderTarget = &this._pRenderTarget3D.GetPinnableReference()) {
                    using var qi = new ComPtr<ID3D11Resource>();
                    this._pDxgiSurface.As(&qi).Ensure();
                    this.Device->CreateRenderTargetView(qi, null, ppRenderTarget).Ensure();
                }
            } catch (Exception e) {
                throw this.LastException = e;
            }
        }
    }

    private void ControlOnForeColorChanged(object? sender, EventArgs e) => this._pForeColorBrush.Reset();

    private void ControlOnBackColorChanged(object? sender, EventArgs e) => this._pBackColorBrush.Reset();

    private void ControlOnFontChanged(object? sender, EventArgs e) => this._pFontTextFormat.Reset();

    private void ControlOnClientSizeChanged(object? sender, EventArgs e)
    {
        this._pRenderTarget2D.Reset();
        this._pRenderTarget3D.Reset();
        SafeDispose.One(ref this._depthStencilResource);
        this._pDxgiSurface.Reset();
    }

    protected abstract void Draw3D(ID3D11RenderTargetView* pRenderTarget);

    protected abstract void Draw2D(ID2D1RenderTarget* pRenderTarget);

    public virtual bool Draw(PaintEventArgs eventArgs)
    {
        try {
            if (this.Control.Width != 0 && this.Control.Height != 0) {
                var viewport = new D3D11_VIEWPORT {
                    //TopLeftX = eventArgs.ClipRectangle.X,
                    //TopLeftY = eventArgs.ClipRectangle.Y,
                    //Width = eventArgs.ClipRectangle.Width,
                    //Height = eventArgs.ClipRectangle.Height,
                    TopLeftX = 0,
                    TopLeftY = 0,
                    Width = this.Control.Width,
                    Height = this.Control.Height,
                    MinDepth = 0f,
                    MaxDepth = 1f,
                };

                var pDepthStencilView = this.DepthStencilView;
                this.DeviceContext->RSSetViewports(1, &viewport);
                this.DeviceContext->RSSetState(this._pRasterizerState);
                var rt = this.RenderTarget3D;
                this.DeviceContext->OMSetRenderTargets(1, &rt, pDepthStencilView);
                this.DeviceContext->OMSetBlendState(this._pBlendState, null, uint.MaxValue);
                if (pDepthStencilView is not null) {
                    this.DeviceContext->ClearDepthStencilView(
                        pDepthStencilView,
                        (uint) D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH,
                        1f,
                        0);
                }

                this.Draw3D(this.RenderTarget3D);

                var pRenderTarget = this.RenderTarget2D;
                pRenderTarget->BeginDraw();
                var errorPending = false;
                try {
                    this.Draw2D(pRenderTarget);
                } catch (Exception) {
                    errorPending = true;
                    throw;
                } finally {
                    var hr = pRenderTarget->EndDraw(null, null);
                    if (!errorPending)
                        hr.Ensure();
                }

                this._pDxgiSwapChain.Get()->Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            this.LastException = e;
            return false;
        }
    }

    protected IDWriteTextLayout* LayoutText(
        out DWRITE_TEXT_METRICS metrics,
        string? @string,
        RectangleF rectangle,
        DWRITE_WORD_WRAPPING? wordWrapping = null,
        DWRITE_TEXT_ALIGNMENT? textAlignment = null,
        DWRITE_PARAGRAPH_ALIGNMENT? paragraphAlignment = null,
        IDWriteTextFormat* textFormat = null)
    {
        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textFormat is null)
            textFormat = this.FontTextFormat;

        if (wordWrapping is not null)
            textFormat->SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat->SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat->SetParagraphAlignment(paragraphAlignment.Value);

        IDWriteTextLayout* layout = null;
        fixed (char* c = (string.IsNullOrEmpty(@string) ? "\0" : @string).AsSpan())
            DWriteFactory->CreateTextLayout(
                c,
                (uint) (string.IsNullOrEmpty(@string) ? 0 : @string.Length),
                textFormat,
                1f * rectangle.Width,
                1f * rectangle.Height,
                &layout).Ensure();
        try {
            fixed (DWRITE_TEXT_METRICS* ptm = &metrics) {
                layout->GetMetrics(ptm).Ensure();
            }

            var layoutCopy = layout;
            layout = null;
            return layoutCopy;
        } finally {
            SafeRelease(ref layout);
        }
    }

    protected void DrawText(
        string? @string,
        RectangleF rectangle,
        DWRITE_WORD_WRAPPING? wordWrapping = null,
        DWRITE_TEXT_ALIGNMENT? textAlignment = null,
        DWRITE_PARAGRAPH_ALIGNMENT? paragraphAlignment = null,
        IDWriteTextFormat* textFormat = null,
        ID2D1Brush* textBrush = null,
        ID2D1Brush* shadowBrush = null,
        float opacity = 1f,
        int borderWidth = 0)
    {
        if (opacity <= 0 || string.IsNullOrWhiteSpace(@string))
            return;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textFormat is null)
            textFormat = this.FontTextFormat;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (textBrush is null)
            textBrush = this.ForeColorBrush;

        // ReSharper disable once ConvertIfStatementToNullCoalescingAssignment
        if (shadowBrush is null)
            shadowBrush = this.BackColorBrush;

        if (wordWrapping is not null)
            textFormat->SetWordWrapping(wordWrapping.Value);

        if (textAlignment is not null)
            textFormat->SetTextAlignment(textAlignment.Value);

        if (paragraphAlignment is not null)
            textFormat->SetParagraphAlignment(paragraphAlignment.Value);

        shadowBrush->SetOpacity(opacity);
        textBrush->SetOpacity(opacity);

        var pRenderTarget = this.RenderTarget2D;
        var scaledBorderWidth = borderWidth <= 0 ? 0 : this.Control.LogicalToDeviceUnits(borderWidth);

        // Lay out once; the shadow is drawn at every offset within the border, which can be many draws on high DPI.
        IDWriteTextLayout* layout = null;
        try {
            fixed (char* pString = @string.AsSpan()) {
                DWriteFactory->CreateGdiCompatibleTextLayout(
                    pString,
                    (uint) @string.Length,
                    textFormat,
                    Math.Max(0f, rectangle.Width),
                    Math.Max(0f, rectangle.Height),
                    1f,
                    null,
                    true,
                    &layout).Ensure();
            }

            for (var i = -scaledBorderWidth; i <= scaledBorderWidth; i++) {
                for (var j = -scaledBorderWidth; j <= scaledBorderWidth; j++) {
                    if (i == 0 && j == 0)
                        continue;
                    pRenderTarget->DrawTextLayout(
                        new(rectangle.Left + i, rectangle.Top + j),
                        layout,
                        shadowBrush,
                        D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
                }
            }

            pRenderTarget->DrawTextLayout(
                new(rectangle.Left, rectangle.Top),
                layout,
                textBrush,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        } finally {
            SafeRelease(ref layout);
        }
    }

    protected ID2D1Brush* CreateSolidColorBrush(Color color)
    {
        ID2D1Brush* pBrush = null;
        var dxgiColor = color.ToDxgiColor();
        this.RenderTarget2D->CreateSolidColorBrush(&dxgiColor, null, (ID2D1SolidColorBrush**) &pBrush).Ensure();
        return pBrush;
    }

    protected ID2D1Brush* GetOrCreateSolidColorBrush(ref ComPtr<ID2D1Brush> pBrush, Color color)
    {
        if (pBrush.IsEmpty())
            pBrush.Attach(this.CreateSolidColorBrush(color));
        return pBrush;
    }

    protected ID2D1Bitmap* CreateFromWicBitmap(ComPtr<IWICBitmapSource> wicBitmapSource)
    {
        ID2D1Bitmap* pBitmap = null;
        if (wicBitmapSource.IsEmpty())
            pBitmap = null;
        else
            this.RenderTarget2D->CreateBitmapFromWicBitmap(wicBitmapSource.Get(), null, &pBitmap).Ensure();

        return pBitmap;
    }

    protected ID2D1Bitmap* GetOrCreateFromWicBitmap(ref ID2D1Bitmap* pBitmap, ComPtr<IWICBitmapSource> wicBitmapSource)
    {
        return pBitmap is null ? this.CreateFromWicBitmap(wicBitmapSource) : pBitmap;
    }

    protected IDWriteTextFormat* GetOrCreateFromFont(ref ComPtr<IDWriteTextFormat> textFormat, Font font)
    {
        if (textFormat.IsEmpty()) {
            fixed (char* pName = font.Name)
            fixed (char* pEmpty = "\0")
            fixed (IDWriteTextFormat** ppFontTextFormat = &textFormat.GetPinnableReference()) {
                DWriteFactory->CreateTextFormat(
                    pName,
                    null,
                    font.Bold
                        ? DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD
                        : DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                    font.Italic
                        ? DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC
                        : DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                    DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                    font.SizeInPoints * this.Control.DeviceDpi / 72,
                    pEmpty,
                    ppFontTextFormat).Ensure();
            }
        }

        return textFormat;
    }
}
