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

public abstract class BaseD2DRenderer : IDisposable {
    protected const int ReattemptCount = 16;
    private static DXGI? _dxgiApi;
    private static D3D11? _d3d11Api;
    private static D2D? _d2dApi;
    private static DWrite? _dwriteApi;
    private static Exception? _apiInitializationException;
    private static ComPtr<ID2D1Factory> _d2d1Factory;
    private static ComPtr<IDWriteFactory> _dwriteFactory;
    private static ComPtr<IDXGIFactory> _dxgiFactory;

    protected static unsafe void TryInitializeApis() {
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

                _dxgiFactory = new(pFactory);
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
                
                _d2d1Factory = new(pFactory);
                break;
            }

            for (var i = ReattemptCount - 1; i >= 0; i--) {
                IDWriteFactory* pFactory = null;
                fixed (Guid* g = &IDWriteFactory.Guid) {
                    Marshal.ThrowExceptionForHR(_dwriteApi.DWriteCreateFactory(
                        Silk.NET.DirectWrite.FactoryType.Isolated, g, (IUnknown**)&pFactory));
                }

                if (pFactory is null) {
                    if (i == 0)
                        throw new FailFastException("???[ID2D1Factory]");
                    continue;
                }
                
                _dwriteFactory = new(pFactory);
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

    protected static unsafe ComPtr<IDXGIFactory> DxgiFactory => _d2d1Factory.Handle is not null
        ? _dxgiFactory
        : throw InitializationException;

    protected static unsafe ComPtr<ID2D1Factory> D2DFactory => _d2d1Factory.Handle is not null
        ? _d2d1Factory
        : throw InitializationException;

    protected static unsafe ComPtr<IDWriteFactory> DWriteFactory => _dwriteFactory.Handle is not null
        ? _dwriteFactory
        : throw InitializationException;

    private static Exception InitializationException => _apiInitializationException ?? new Exception("Uninitialized");

    private class NullNativeWindowSource : INativeWindowSource {
        public INativeWindow? Native => null;
    }
}

public abstract class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    protected readonly T Control;

    private ComPtr<IDXGIAdapter> _adapter;
    private ComPtr<ID3D11Device> _d3dDevice;
    private ComPtr<IDXGISwapChain> _dxgiSwapChain;
    private ComPtr<ID3D11DeviceContext> _d3dContext;
    private ComPtr<IDXGISurface> _dxgiSurface;
    private ComPtr<ID2D1RenderTarget> _renderTarget;

    private ComPtr<ID2D1SolidColorBrush> _foreColorBrush;
    private Color _foreColor;

    private ComPtr<ID2D1SolidColorBrush> _backColorBrush;
    private Color _backColor;

    private ComPtr<IDWriteTextFormat> _fontTextFormat;
    private Font _font = null!;

    protected unsafe BaseD2DRenderer(T control) {
        Control = control;

        try {
            TryInitializeApis();

            while (true) {
                if (DxgiFactory.EnumAdapters(0u, ref _adapter) >= 0)
                    break;

                Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory(out ComPtr<IDXGIFactory1> dxgiFac1));
                using (dxgiFac1) {
                    ComPtr<IDXGIAdapter1> adapter1 = new();
                    Marshal.ThrowExceptionForHR(dxgiFac1.EnumAdapters1(0u, ref adapter1));
                    using (adapter1) {
                        if (adapter1.QueryInterface(out _adapter) >= 0)
                            break;
                        Marshal.ThrowExceptionForHR(DxgiFactory.QueryInterface(out ComPtr<IDXGIFactory4> dxgiFac4));
                        using (dxgiFac4)
                            Marshal.ThrowExceptionForHR(dxgiFac4.EnumWarpAdapter(out _adapter));
                    }
                }

                break;
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

        _foreColorBrush.Dispose();
        _foreColorBrush = null;
        _backColorBrush.Dispose();
        _backColorBrush = null;
        _d3dDevice.Dispose();
        _d3dDevice = null;
        _dxgiSwapChain.Dispose();
        _dxgiSwapChain = null;
        _d3dContext.Dispose();
        _d3dContext = null;
        _renderTarget.Dispose();
        _renderTarget = null;
        _dxgiSurface.Dispose();
        _dxgiSurface = null;
        _adapter.Dispose();
        _adapter = null;
    }

    public Exception? LastException { get; protected set; }

    public Color ForeColor {
        get => _foreColor;
        set {
            if (_foreColor == value)
                return;
            _foreColor = value;
            _foreColorBrush.Dispose();
            _foreColorBrush = null;
        }
    }

    protected unsafe ComPtr<ID2D1SolidColorBrush> ForeColorBrush {
        get {
            if (_foreColorBrush.Handle is null)
                Marshal.ThrowExceptionForHR(RenderTarget.CreateSolidColorBrush(
                    new D3Dcolorvalue(ForeColor.R / 255f, ForeColor.G / 255f, ForeColor.B / 255f, ForeColor.A / 255f),
                    null,
                    ref _foreColorBrush));
            return _foreColorBrush;
        }
    }

    public Color BackColor {
        get => _backColor;
        set {
            if (_backColor == value)
                return;
            _backColor = value;
            _backColorBrush.Dispose();
            _backColorBrush = null;
        }
    }

    protected unsafe ComPtr<ID2D1SolidColorBrush> BackColorBrush {
        get {
            if (_backColorBrush.Handle is null)
                Marshal.ThrowExceptionForHR(RenderTarget.CreateSolidColorBrush(
                    new D3Dcolorvalue(BackColor.R / 255f, BackColor.G / 255f, BackColor.B / 255f, BackColor.A / 255f),
                    null,
                    ref _backColorBrush));
            return _backColorBrush;
        }
    }

    public Font Font {
        get => _font;
        set {
            if (Equals(_font, value))
                return;
            _font = value;
            _fontTextFormat.Dispose();
            _fontTextFormat = null;
        }
    }

    protected unsafe ComPtr<IDWriteTextFormat> FontTextFormat {
        get {
            if (_fontTextFormat.Handle is null) {
                // seems that silk doesn't properly set variables being passed around as fixed
                var name = Font.Name.ToCharArray();
                var empty = new char[1];
                fixed (char* pName = name)
                fixed (char* pEmpty = empty) {
                    try {
                        IDWriteTextFormat* p = null;
                        Marshal.ThrowExceptionForHR(DWriteFactory.CreateTextFormat(
                            pName,
                            (IDWriteFontCollection*) null,
                            Font.Bold ? FontWeight.Bold : FontWeight.Normal,
                            Font.Italic ? FontStyle.Italic : FontStyle.Normal,
                            FontStretch.Normal,
                            Font.SizeInPoints * 4 / 3,
                            pEmpty,
                            &p));
                        _fontTextFormat = new(p);
                    } catch (Exception e) {
                        LastException = e;
                        throw;
                    }
                }
            }

            return _fontTextFormat;
        }
    }

    protected unsafe ComPtr<ID2D1RenderTarget> RenderTarget {
        get {
            if (_renderTarget.Handle is not null)
                return _renderTarget;

            try {
                if (_dxgiSwapChain.Handle is null) {
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
                            Width = (uint) Control.Width,
                            Height = (uint) Control.Height,
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
                        _dxgiSwapChain.Dispose();
                        _dxgiSwapChain = null;
                        _d3dDevice.Dispose();
                        _d3dDevice = null;
                        _d3dContext.Dispose();
                        _d3dContext = null;
                        fixed (D3DFeatureLevel* pLevels = levels) {
                            Marshal.ThrowExceptionForHR(D3D11.CreateDeviceAndSwapChain(
                                _adapter,
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

                        if (pSwapChain is null || pDevice is null || pSwapChain is null) {
                            if (pSwapChain is not null)
                                pSwapChain->Release();
                            if (pDevice is not null)
                                pDevice->Release();
                            if (pSwapChain is not null)
                                pSwapChain->Release();
                            if (i == 0)
                                throw new FailFastException("???[CreateDeviceAndSwapChain]");
                            continue;
                        }

                        _dxgiSwapChain = new(pSwapChain);
                        _d3dDevice = new(pDevice);
                        _d3dContext = new(pContext);
                    }
                } else {
                    Marshal.ThrowExceptionForHR(_dxgiSwapChain.ResizeBuffers(
                        1,
                        (uint) Math.Max(16, Control.Width),
                        (uint) Math.Max(16, Control.Height),
                        Format.FormatB8G8R8A8Unorm,
                        0));
                }

                Marshal.ThrowExceptionForHR(_dxgiSwapChain.GetBuffer(0, out _dxgiSurface));
                var rtp = new RenderTargetProperties {
                    Type = RenderTargetType.Default,
                    PixelFormat = new() {
                        AlphaMode = AlphaMode.Premultiplied,
                        Format = Format.FormatUnknown,
                    },
                    DpiX = Control.DeviceDpi,
                    DpiY = Control.DeviceDpi,
                    Usage = RenderTargetUsage.GdiCompatible
                };
                // for some reason it returns success but renderTarget is null occasionally
                for (var i = ReattemptCount - 1; i >= 0; i--) {
                    ID2D1RenderTarget* pRenderTarget = null;
                    Marshal.ThrowExceptionForHR(D2DFactory.CreateDxgiSurfaceRenderTarget(
                        _dxgiSurface.Handle, &rtp, &pRenderTarget));
                    if (pRenderTarget is null && i == 0)
                        throw new FailFastException("???[CreateDxgiSurfaceRenderTarget]");
                    _renderTarget = new(pRenderTarget);
                }

                if (_renderTarget.Handle is null)
                    throw new("Fail");

                return _renderTarget;
            } catch (Exception e) {
                LastException = e;
                throw;
            }
        }
    }

    protected void ControlOnResize(object? sender, EventArgs e) {
        _renderTarget.Dispose();
        _renderTarget = null;
        _dxgiSurface.Dispose();
        _dxgiSurface = null;
        try {
            // Try to preload
            _ = RenderTarget;
        } catch (Exception) {
            LastException = null;
        }

        Control.Invalidate();
    }

    protected abstract void DrawInternal();

    public bool Draw(PaintEventArgs _) {
        try {
            if (Control.Width != 0 && Control.Height != 0) {
                RenderTarget.BeginDraw();
                var errorPending = false;
                try {
                    DrawInternal();
                } catch (Exception) {
                    errorPending = true;
                    throw;
                } finally {
                    var hr = RenderTarget.EndDraw(new Span<ulong>(), new Span<ulong>());
                    if (!errorPending)
                        Marshal.ThrowExceptionForHR(hr);
                }

                _dxgiSwapChain.Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            LastException = e;
            return false;
        }
    }
}
