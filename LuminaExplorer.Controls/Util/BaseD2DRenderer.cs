using System.Diagnostics;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.Util;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using AlphaMode = Silk.NET.Direct2D.AlphaMode;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteFactory = Silk.NET.DirectWrite.IDWriteFactory;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;

namespace LuminaExplorer.Controls.Util;

public abstract unsafe class BaseD2DRenderer : IDisposable {
    protected const int ReattemptCount = 16;
    private static DXGI? _dxgiApi;
    private static D3D11? _d3d11Api;
    private static D2D? _d2dApi;
    private static DWrite? _dwriteApi;
    private static Exception? _apiInitializationException;
    private static ID2D1Factory* _d2d1Factory;
    private static IDWriteFactory* _dwriteFactory;
    private static IDXGIFactory* _dxgiFactory;

    protected static void TryInitializeApis() {
        if (_apiInitializationException is not null)
            throw _apiInitializationException;

        try {
            _d2dApi = D2D.GetApi();
            _d3d11Api = D3D11.GetApi(new NullNativeWindowSource());
            _dxgiApi = DXGI.GetApi(new NullNativeWindowSource());
            _dwriteApi = DWrite.GetApi();

            for (var i = ReattemptCount - 1; i >= 0; i--) {
                IDXGIFactory* pFactory = null;
                fixed (Guid* g = &IDXGIFactory.Guid)
                    Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory(g, (void**) &pFactory));
                if (pFactory is null) {
                    if (i == 0)
                        throw new FailFastException("???[IDXGIFactory]");
                    continue;
                }

                _dxgiFactory = pFactory;
                break;
            }

            for (var i = ReattemptCount - 1; i >= 0; i--) {
                ID2D1Factory* pFactory = null;
                var fo = new FactoryOptions();
                fixed (Guid* g = &ID2D1Factory.Guid) {
                    Marshal.ThrowExceptionForHR(D2D.D2D1CreateFactory(
                        Silk.NET.Direct2D.FactoryType.SingleThreaded, g, &fo, (void**) &pFactory));
                }

                if (pFactory is null) {
                    if (i == 0)
                        throw new FailFastException("???[ID2D1Factory]");
                    continue;
                }

                _d2d1Factory = pFactory;
                break;
            }

            for (var i = ReattemptCount - 1; i >= 0; i--) {
                IDWriteFactory* pFactory = null;
                fixed (Guid* g = &IDWriteFactory.Guid) {
                    Marshal.ThrowExceptionForHR(_dwriteApi.DWriteCreateFactory(
                        Silk.NET.DirectWrite.FactoryType.Isolated, g, (IUnknown**) &pFactory));
                }

                if (pFactory is null) {
                    if (i == 0)
                        throw new FailFastException("???[ID2D1Factory]");
                    continue;
                }

                _dwriteFactory = pFactory;
                break;
            }
        } catch (Exception e) {
            _apiInitializationException = e;
            throw;
        }
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) { }
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~BaseD2DRenderer() {
        Dispose(false);
    }

    protected static DXGI Dxgi => _dxgiApi ?? throw InitializationException;
    protected static D3D11 D3D11 => _d3d11Api ?? throw InitializationException;
    protected static D2D D2D => _d2dApi ?? throw InitializationException;
    protected static DWrite DWrite => _dwriteApi ?? throw InitializationException;

    protected static IDXGIFactory* DxgiFactory => _d2d1Factory is not null
        ? _dxgiFactory
        : throw InitializationException;

    protected static ID2D1Factory* D2DFactory => _d2d1Factory is not null
        ? _d2d1Factory
        : throw InitializationException;

    protected static IDWriteFactory* DWriteFactory => _dwriteFactory is not null
        ? _dwriteFactory
        : throw InitializationException;

    private static Exception InitializationException => _apiInitializationException ?? new Exception("Uninitialized");

    protected static void SafeRelease<T>(ref T* u) where T : unmanaged {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
    }

    private class NullNativeWindowSource : INativeWindowSource {
        public INativeWindow? Native => null;
    }
}

public abstract unsafe class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    protected readonly T Control;

    private IDXGIAdapter* _pAdapter;
    private ID3D11Device* _pD3dDevice;
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

            IDXGIAdapter* pAdapter = null;
            try {
                if (DxgiFactory->EnumAdapters(0u, &pAdapter) < 0) {
                    IDXGIFactory1* pFactory1 = null;
                    fixed (Guid* g = &IDXGIFactory1.Guid)
                        Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory(g, (void**) &pFactory1));
                    try {
                        IDXGIAdapter1* pAdapter1 = null;
                        Marshal.ThrowExceptionForHR(pFactory1->EnumAdapters1(0u, &pAdapter1));
                        try {
                            fixed (Guid* g = &IDXGIAdapter1.Guid) {
                                if (pAdapter1->QueryInterface(g, (void**) &pAdapter) < 0) {
                                    IDXGIFactory4* pDxgiFac4 = null;
                                    fixed (Guid* g2 = &IDXGIFactory4.Guid)
                                        Marshal.ThrowExceptionForHR(
                                            DxgiFactory->QueryInterface(g2, (void**) &pDxgiFac4));

                                    try {
                                        fixed (Guid* g2 = &IDXGIAdapter.Guid)
                                            Marshal.ThrowExceptionForHR(pDxgiFac4->EnumWarpAdapter(g2, (void**) &pAdapter));
                                    } finally {
                                        SafeRelease(ref pDxgiFac4);
                                    }
                                }
                            }
                        } finally {
                            SafeRelease(ref pAdapter1);
                        }
                    } finally {
                        SafeRelease(ref pFactory1);
                    }
                }

                _pAdapter = pAdapter;
                pAdapter = null;
            } finally {
                SafeRelease(ref pAdapter);
            }

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
        SafeRelease(ref _pD3dDevice);
        SafeRelease(ref _pDxgiSwapChain);
        SafeRelease(ref _pD3dContext);
        SafeRelease(ref _pRenderTarget);
        SafeRelease(ref _pDxgiSurface);
        SafeRelease(ref _pAdapter);
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

    protected ID2D1Brush* ForeColorBrush {
        get {
            if (_pForeColorBrush is null) {
                ID2D1SolidColorBrush* pBrush = null;
                Marshal.ThrowExceptionForHR(RenderTarget->CreateSolidColorBrush(
                    new D3Dcolorvalue(ForeColor.R / 255f, ForeColor.G / 255f, ForeColor.B / 255f, ForeColor.A / 255f),
                    null,
                    &pBrush));
                _pForeColorBrush = (ID2D1Brush*) pBrush;
            }

            return _pForeColorBrush;
        }
    }

    public Color BackColor {
        get => _backColor;
        set {
            if (_backColor == value)
                return;
            _backColor = value;
            SafeRelease(ref _pBackColorBrush);
        }
    }

    protected ID2D1Brush* BackColorBrush {
        get {
            if (_pBackColorBrush is null) {
                ID2D1SolidColorBrush* pBrush = null;
                Marshal.ThrowExceptionForHR(RenderTarget->CreateSolidColorBrush(
                    new D3Dcolorvalue(BackColor.R / 255f, BackColor.G / 255f, BackColor.B / 255f, BackColor.A / 255f),
                    null,
                    &pBrush));
                _pBackColorBrush = (ID2D1Brush*) pBrush;
            }

            return _pBackColorBrush;
        }
    }

    public Font Font {
        get => _font;
        set {
            if (Equals(_font, value))
                return;
            _font = value;
            SafeRelease(ref _pFontTextFormat);
        }
    }

    protected IDWriteTextFormat* FontTextFormat {
        get {
            if (_pFontTextFormat is null) {
                // seems that silk doesn't properly set variables being passed around as fixed
                var name = Font.Name.ToCharArray();
                var empty = new char[1];
                fixed (char* pName = name)
                fixed (char* pEmpty = empty) {
                    try {
                        IDWriteTextFormat* p = null;
                        Marshal.ThrowExceptionForHR(DWriteFactory->CreateTextFormat(
                            pName,
                            null,
                            Font.Bold ? FontWeight.Bold : FontWeight.Normal,
                            Font.Italic ? FontStyle.Italic : FontStyle.Normal,
                            FontStretch.Normal,
                            Font.SizeInPoints * 4 / 3,
                            pEmpty,
                            &p));
                        _pFontTextFormat = p;
                    } catch (Exception e) {
                        LastException = e;
                        throw;
                    }
                }
            }

            return _pFontTextFormat;
        }
    }

    protected ID2D1RenderTarget* RenderTarget {
        get {
            if (_pRenderTarget is not null)
                return _pRenderTarget;

            try {
                if (_pDxgiSwapChain is null) {
                    var levels = new[] {
                        D3DFeatureLevel.Level111,
                        D3DFeatureLevel.Level110,
                        D3DFeatureLevel.Level101,
                        D3DFeatureLevel.Level100,
                        D3DFeatureLevel.Level93,
                        D3DFeatureLevel.Level92,
                        D3DFeatureLevel.Level91,
                    };

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

                    for (var i = ReattemptCount - 1; i >= 0; i--) {
                        IDXGISwapChain* pSwapChain = null;
                        ID3D11Device* pDevice = null;
                        ID3D11DeviceContext* pContext = null;
                        SafeRelease(ref _pDxgiSwapChain);
                        SafeRelease(ref _pD3dDevice);
                        SafeRelease(ref _pD3dContext);
                        fixed (D3DFeatureLevel* pLevels = levels) {
                            Marshal.ThrowExceptionForHR(D3D11.CreateDeviceAndSwapChain(
                                _pAdapter,
                                // This method returns E_INVALIDARG if you set the pAdapter parameter to a
                                // non-NULL value and the DriverType parameter to the D3D_DRIVER_TYPE_HARDWARE
                                // value.
                                0,
                                nint.Zero,
                                (uint) (CreateDeviceFlag.Debug | CreateDeviceFlag.BgraSupport),
                                pLevels,
                                (uint) levels.Length,
                                D3D11.SdkVersion,
                                desc,
                                &pSwapChain,
                                &pDevice,
                                null,
                                &pContext));
                        }

                        if (pSwapChain is null || pDevice is null || pContext is null) {
                            SafeRelease(ref pSwapChain);
                            SafeRelease(ref pDevice);
                            SafeRelease(ref pContext);
                            if (i == 0)
                                throw new FailFastException("???[CreateDeviceAndSwapChain]");
                            continue;
                        }

                        _pDxgiSwapChain = pSwapChain;
                        _pD3dDevice = pDevice;
                        _pD3dContext = pContext;
                    }
                }

                SafeRelease(ref _pRenderTarget);
                SafeRelease(ref _pDxgiSurface);

                try {
                    Marshal.ThrowExceptionForHR(_pDxgiSwapChain->ResizeBuffers(0, 0, 0, Format.FormatUnknown, 0));
                } catch (Exception e) {
                    Debug.Print(e.ToString());
                }

                IDXGISurface* pNewSurface = null;
                fixed (Guid* g = &IDXGISurface.Guid)
                    Marshal.ThrowExceptionForHR(_pDxgiSwapChain->GetBuffer(0, g, (void**) &pNewSurface));
                _pDxgiSurface = pNewSurface;

                var rtp = new RenderTargetProperties {
                    Type = RenderTargetType.Default,
                    PixelFormat = new() {
                        AlphaMode = AlphaMode.Premultiplied,
                        Format = Format.FormatUnknown,
                    },
                    DpiX = Control.DeviceDpi,
                    DpiY = Control.DeviceDpi,
                };
                // for some reason it returns success but renderTarget is null occasionally
                for (var i = ReattemptCount - 1; i >= 0; i--) {
                    ID2D1RenderTarget* pRenderTarget = null;
                    Marshal.ThrowExceptionForHR(D2DFactory->CreateDxgiSurfaceRenderTarget(
                        _pDxgiSurface, &rtp, &pRenderTarget));
                    if (pRenderTarget is null) {
                        if (i == 0)
                            throw new FailFastException("???[CreateDxgiSurfaceRenderTarget]");
                        continue;
                    }

                    _pRenderTarget = pRenderTarget;
                    break;
                }

                if (_pRenderTarget is null)
                    throw new("Fail");

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

        try {
            _ = RenderTarget;
        } catch (Exception) {
            // pass
        }

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
                        Marshal.ThrowExceptionForHR(hr);
                }

                _pDxgiSwapChain->Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            LastException = e;
            return false;
        }
    }
}
