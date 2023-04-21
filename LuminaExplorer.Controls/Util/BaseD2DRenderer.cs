using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using AlphaMode = Silk.NET.Direct2D.AlphaMode;

namespace LuminaExplorer.Controls.Util;

public abstract class BaseD2DRenderer : IDisposable {
    private static DXGI? _dxgiApi;
    private static D3D11? _d3d11Api;
    private static D2D? _d2dApi;
    private static Exception? _apiInitializationException;

    protected static void TryInitializeApis() {
        if (_apiInitializationException is not null)
            throw _apiInitializationException;

        try {
            _d2dApi = D2D.GetApi();
            _d3d11Api = D3D11.GetApi(new NullNativeWindowSource());
            _dxgiApi = DXGI.GetApi(new NullNativeWindowSource());
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

    protected static DXGI Dxgi => _dxgiApi ?? throw (_apiInitializationException ?? new Exception("Uninitialized"));
    protected static D3D11 D3D11 => _d3d11Api ?? throw (_apiInitializationException ?? new Exception("Uninitialized"));
    protected static D2D D2D => _d2dApi ?? throw (_apiInitializationException ?? new Exception("Uninitialized"));

    private class NullNativeWindowSource : INativeWindowSource {
        public INativeWindow? Native => null;
    }
}

public abstract class BaseD2DRenderer<T> : BaseD2DRenderer where T : Control {
    protected readonly T Control;

    private ComPtr<ID3D11Device> _d3dDevice;
    private ComPtr<IDXGISwapChain> _dxgiSwapChain;
    private ComPtr<ID3D11DeviceContext> _d3dContext;
    private ComPtr<IDXGISurface> _dxgiSurface;
    private ComPtr<ID2D1RenderTarget> _renderTarget;
    private ComPtr<ID2D1Factory> _d2d1Factory;

    private ComPtr<ID2D1SolidColorBrush> _foreColorBrush;
    private ComPtr<ID2D1SolidColorBrush> _backColorBrush;
    private Color _foreColor;
    private Color _backColor;

    protected unsafe BaseD2DRenderer(T control) {
        Control = control;

        try {
            TryInitializeApis();

            Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory(out ComPtr<IDXGIFactory> dxgiFac));
            using (dxgiFac) {
                ComPtr<IDXGIAdapter> adapter = new();
                while (true) {
                    if (dxgiFac.EnumAdapters(0u, ref adapter) >= 0)
                        break;

                    Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory(out ComPtr<IDXGIFactory1> dxgiFac1));
                    using (dxgiFac1) {
                        ComPtr<IDXGIAdapter1> adapter1 = new();
                        Marshal.ThrowExceptionForHR(dxgiFac1.EnumAdapters1(0u, ref adapter1));
                        using (adapter1) {
                            if (adapter1.QueryInterface(out adapter) >= 0)
                                break;
                            Marshal.ThrowExceptionForHR(dxgiFac.QueryInterface(out ComPtr<IDXGIFactory4> dxgiFac4));
                            using (dxgiFac4)
                                Marshal.ThrowExceptionForHR(dxgiFac4.EnumWarpAdapter(out adapter));
                        }
                    }

                    break;
                }

                using (adapter) {
                    var levels = new[] {
                        D3DFeatureLevel.Level111,
                        D3DFeatureLevel.Level110,
                        D3DFeatureLevel.Level101,
                        D3DFeatureLevel.Level100,
                        D3DFeatureLevel.Level93,
                        D3DFeatureLevel.Level92,
                        D3DFeatureLevel.Level91,
                    };
                    fixed (D3DFeatureLevel* pLevels = levels) {
                        Marshal.ThrowExceptionForHR(D3D11.CreateDevice(
                            adapter,
                            // This method returns E_INVALIDARG if you set the pAdapter parameter to a
                            // non-NULL value and the DriverType parameter to the D3D_DRIVER_TYPE_HARDWARE
                            // value.
                            0,
                            nint.Zero,
                            (uint) (CreateDeviceFlag.Debug | CreateDeviceFlag.BgraSupport),
                            pLevels,
                            (uint) levels.Length,
                            D3D11.SdkVersion,
                            ref _d3dDevice,
                            null,
                            ref _d3dContext));
                    }

                    var desc = new SwapChainDesc {
                        BufferDesc = new() {
                            Width = (uint) Math.Max(16, Control.Width),
                            Height = (uint) Math.Max(16, Control.Height),
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
                    Marshal.ThrowExceptionForHR(dxgiFac.CreateSwapChain(
                        _d3dDevice, ref desc, ref _dxgiSwapChain));
                }

                // Marshal.ThrowExceptionForHR(_d3dDevice.QueryInterface(out _dxgiDevice));
                // var cp = new CreationProperties(ThreadingMode.SingleThreaded, DebugLevel.Information);
                // Marshal.ThrowExceptionForHR(_d2dApi.D2D1CreateDevice(ref _dxgiDevice.Get(), cp, ref _d2d1Device));

                void* dfactory = null;
                var guid = ID2D1Factory.Guid;
                var fo = new FactoryOptions();
                Marshal.ThrowExceptionForHR(D2D.D2D1CreateFactory(
                    FactoryType.SingleThreaded, ref guid, fo, ref dfactory));
                _d2d1Factory = new((ID2D1Factory*) dfactory);
            }

            Control.Resize += ControlOnResize;

            ForeColor = Control.ForeColor;
            BackColor = Control.BackColor;
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
        _d2d1Factory.Dispose();
        _d2d1Factory = null;
    }

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

    protected unsafe ComPtr<ID2D1RenderTarget> RenderTarget {
        get {
            if (_renderTarget.Handle is not null)
                return _renderTarget;
            try {
                Marshal.ThrowExceptionForHR(_dxgiSwapChain.ResizeBuffers(
                    1,
                    (uint) Math.Max(16, Control.Width),
                    (uint) Math.Max(16, Control.Height),
                    Format.FormatB8G8R8A8Unorm,
                    0));
                Marshal.ThrowExceptionForHR(_dxgiSwapChain.GetBuffer(0, out _dxgiSurface));
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
                for (var i = 0; i < 10 && _renderTarget.Handle is null; i++) {
                    Marshal.ThrowExceptionForHR(_d2d1Factory.CreateDxgiSurfaceRenderTarget(
                        _dxgiSurface.Handle, rtp, ref _renderTarget));
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

    public Exception? LastException { get; protected set; }

    protected void ControlOnResize(object? sender, EventArgs e) {
        _renderTarget.Dispose();
        _renderTarget = null;
        _dxgiSurface.Dispose();
        _dxgiSurface = null;
        Control.Invalidate();
    }

    protected abstract void DrawInternal();

    public bool Draw(PaintEventArgs _) {
        try {
            if (Control.Width != 0 && Control.Height != 0) {
                RenderTarget.BeginDraw();
                DrawInternal();
                Marshal.ThrowExceptionForHR(RenderTarget.EndDraw(new Span<ulong>(), new Span<ulong>()));
                _dxgiSwapChain.Present(0, 0);
            }

            return true;
        } catch (Exception e) {
            LastException = e;
            return false;
        }
    }
}
