using System;
using System.Linq;
using DirectN;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.Util;

public abstract class BaseD2DRenderer : IDisposable {
    private static Exception? _apiInitializationException;

    private static ID2D1Factory? _pD2D1Factory;
    private static IDWriteFactory? _pDWriteFactory;
    private static IDXGIFactory? _pDxgiFactory;
    private static ID3D11Device? _pSharedD3D11Device;

    protected static void TryInitializeApis() {
        if (_apiInitializationException is not null)
            throw _apiInitializationException;

        try {
            if (DXGIFunctions.CreateDXGIFactory(typeof(IDXGIFactory4).GUID, out var pFactory).IsError) {
                if (DXGIFunctions.CreateDXGIFactory(typeof(IDXGIFactory1).GUID, out pFactory).IsError) {
                    DXGIFunctions.CreateDXGIFactory(typeof(IDXGIFactory).GUID, out pFactory).ThrowOnError();
                }
            }

            _pDxgiFactory = (IDXGIFactory)pFactory;
            var d2D1FactoryOptions = new D2D1_FACTORY_OPTIONS();
            D2D1Functions.D2D1CreateFactory(
                D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_MULTI_THREADED,
                typeof(ID2D1Factory).GUID,
                ref d2D1FactoryOptions,
                out pFactory).ThrowOnError();
            _pD2D1Factory = (ID2D1Factory) pFactory;
            _pDWriteFactory = DWriteFunctions.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED).Object;

            using var anyAdapter = CreateAnyAvailableDxgiAdapter();
            Direct3DDeviceBuilder
                .DisposeOnException()
                .WithAdapter(anyAdapter)
                .WithFlagAdd(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG)
                .WithFlagAdd(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT)
                .Create()
                .TakeDevice(out var d)
                .Dispose();
            _pSharedD3D11Device = d.Object;
        } catch (Exception e) {
            _apiInitializationException = e;
            throw;
        }
    }

    protected virtual void Dispose(bool disposing) { }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~BaseD2DRenderer() {
        Dispose(false);
    }

    protected static IDXGIFactory DxgiFactory => _pDxgiFactory ?? throw InitializationException;
    protected static ID2D1Factory D2DFactory => _pD2D1Factory ?? throw InitializationException;
    protected static IDWriteFactory DWriteFactory => _pDWriteFactory ?? throw InitializationException;
    protected static ID3D11Device SharedD3D11Device => _pSharedD3D11Device ?? throw InitializationException;

    private static Exception InitializationException => _apiInitializationException ?? new Exception("Uninitialized");

    protected static IComObject<IDXGIAdapter> CreateAnyAvailableDxgiAdapter() {
        if (DxgiFactory.EnumAdapters<IDXGIAdapter>().FirstOrDefault() is { } a0)
            return a0;
        if (((IDXGIFactory1)DxgiFactory).EnumAdapters1<IDXGIAdapter1>().FirstOrDefault() is { } a1)
            return a1;

        ((IDXGIFactory4)DxgiFactory).EnumWarpAdapter(typeof(IDXGIAdapter1).GUID, out var adapter).ThrowOnError();
        return new ComObject<IDXGIAdapter1>((IDXGIAdapter1) adapter);
    }

    protected sealed class Direct3DDeviceBuilder : IDisposable {
        private readonly bool _disposeOnException;

        private D3D_FEATURE_LEVEL _minimumFeatureLevel = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1;
        private D3D_DRIVER_TYPE _driverType = D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN;
        private D3D11_CREATE_DEVICE_FLAG _flags;

        // in, no ownership
        private IComObject<IDXGIAdapter>? _pAdapter;

        // out
        private D3D_FEATURE_LEVEL _obtainedFeatureLevel = 0;

        // out, refcounted
        private IComObject<ID3D11Device>? _pDevice;
        private IComObject<ID3D11DeviceContext>? _pContext;

        private Direct3DDeviceBuilder(bool disposeOnException) {
            _disposeOnException = disposeOnException;
        }

        public static Direct3DDeviceBuilder ManualDispose() => new(false);
        public static Direct3DDeviceBuilder DisposeOnException() => new(true);

        public void Dispose() => Clear();

        public Direct3DDeviceBuilder Clear() {
            SafeDispose.One(ref _pDevice);
            SafeDispose.One(ref _pContext);
            _obtainedFeatureLevel = 0;
            return this;
        }

        public Direct3DDeviceBuilder WithMinimumFeatureLevel(D3D_FEATURE_LEVEL minimumFeatureLevel) {
            _minimumFeatureLevel = minimumFeatureLevel;
            return this;
        }

        public Direct3DDeviceBuilder WithDriverType(D3D_DRIVER_TYPE driverType) {
            try {
                _pAdapter = null;
                _driverType = driverType;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithAdapter(IComObject<IDXGIAdapter> pAdapter) {
            _pAdapter = pAdapter;
            _driverType = D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagReplace(D3D11_CREATE_DEVICE_FLAG flags) {
            _flags = flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagAdd(D3D11_CREATE_DEVICE_FLAG flags) {
            _flags |= flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagRemove(D3D11_CREATE_DEVICE_FLAG flags) {
            _flags &= ~flags;
            return this;
        }

        public Direct3DDeviceBuilder Create() {
            try {
                var levels = new[] {
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1,
                }.TakeWhile(x => x >= _minimumFeatureLevel).ToArray();

                SafeDispose.One(ref _pDevice);
                SafeDispose.One(ref _pContext);
                D3D11Functions.D3D11CreateDevice(
                    _pAdapter?.Object,
                    _driverType,
                    nint.Zero,
                    (uint) _flags,
                    levels,
                    (uint) levels.Length,
                    D3D11Constants.D3D11_SDK_VERSION,
                    out var device,
                    out _obtainedFeatureLevel,
                    out var context).ThrowOnError();
                _pDevice = new ComObject<ID3D11Device>(device);
                _pContext = new ComObject<ID3D11DeviceContext>(context);
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDevice(out IComObject<ID3D11Device> pDevice) {
            try {
                pDevice = _pDevice ?? throw new NullReferenceException();
                _pDevice = null;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeContext(out IComObject<ID3D11DeviceContext> pContext) {
            try {
                pContext = _pContext ?? throw new NullReferenceException();
                _pContext = null;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDeviceLevel(out D3D_FEATURE_LEVEL featureLevel) {
            try {
                if (_obtainedFeatureLevel == 0)
                    throw new NullReferenceException();
                featureLevel = _obtainedFeatureLevel;
                _obtainedFeatureLevel = 0;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }
    }
}
