using System;
using System.Linq;
using System.Reflection;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff;

public abstract unsafe class DirectXObject : IDisposable {
    private static Exception? _apiInitializationException;

    private static ComPtr<ID2D1Factory> _pD2D1Factory;
    private static ComPtr<IDWriteFactory> _pDWriteFactory;
    private static ComPtr<IDXGIFactory> _pDxgiFactory;
    private static ComPtr<ID3D11Device> _pSharedD3D11Device;
    private static ComPtr<ID3D11DeviceContext> _pSharedD3D11Context;

    protected static void TryInitializeApis()
    {
        if (_apiInitializationException is not null)
            throw _apiInitializationException;

        try {
            if (_pDxgiFactory.IsEmpty()) {
                fixed (void* ppFactory = &_pDxgiFactory.GetPinnableReference())
                fixed (Guid* g = &IID.IID_IDXGIFactory)
                    DirectX.CreateDXGIFactory(g, (void**) ppFactory).Ensure();
            }

            if (_pD2D1Factory.IsEmpty()) {
                fixed (void* ppFactory = &_pD2D1Factory.GetPinnableReference())
                fixed (Guid* g = &IID.IID_ID2D1Factory) {
                    var fo = new D2D1_FACTORY_OPTIONS();
                    DirectX.D2D1CreateFactory(
                        D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                        g,
                        &fo,
                        (void**) ppFactory).Ensure();
                }
            }

            if (_pDWriteFactory.IsEmpty()) {
                fixed (void* ppFactory = &_pDWriteFactory.GetPinnableReference())
                fixed (Guid* g = &IID.IID_IDWriteFactory) {
                    DirectX.DWriteCreateFactory(
                        DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED,
                        g,
                        (IUnknown**) ppFactory).Ensure();
                }
            }

            if (_pSharedD3D11Device.IsEmpty() || _pSharedD3D11Context.IsEmpty()) {
                _pSharedD3D11Device.Reset();
                _pSharedD3D11Context.Reset();
                Direct3DDeviceBuilder
                    .DisposeOnException()
                    .WithAdapterTakeOwnership(GetAnyAvailableDxgiAdapter())
                    .WithFlagAdd(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_DEBUG)
                    .WithFlagAdd(D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT)
                    .Create()
                    .TakeDevice(out _pSharedD3D11Device)
                    .TakeContext(out _pSharedD3D11Context)
                    .Dispose();

                using var dev2 = new ComPtr<IDXGIDevice1>();
                if (_pSharedD3D11Device.As(&dev2).SUCCEEDED)
                    dev2.Get()->SetMaximumFrameLatency(1);
            }
        } catch (Exception e) {
            _apiInitializationException = e;
            throw;
        }
    }

    protected virtual void Dispose(bool disposing)
    { }

    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~DirectXObject()
    {
        this.Dispose(false);
    }

    protected static IDXGIFactory* DxgiFactory => _pD2D1Factory.IsEmpty()
        ? throw InitializationException
        : _pDxgiFactory;

    protected static ID2D1Factory* D2DFactory => _pD2D1Factory.IsEmpty()
        ? throw InitializationException
        : _pD2D1Factory;

    protected static IDWriteFactory* DWriteFactory => _pDWriteFactory.IsEmpty()
        ? throw InitializationException
        : _pDWriteFactory;

    protected static ID3D11Device* SharedD3D11Device => _pSharedD3D11Device.IsEmpty()
        ? throw InitializationException
        : _pSharedD3D11Device;

    protected static ID3D11DeviceContext* SharedD3D11DeviceContext => _pSharedD3D11Context.IsEmpty()
        ? throw InitializationException
        : _pSharedD3D11Context;

    private static Exception InitializationException => _apiInitializationException ?? new Exception("Uninitialized");

    protected static IDXGIAdapter* GetAnyAvailableDxgiAdapter()
    {
        IDXGIAdapter* pAdapter = null;
        IDXGIFactory1* pFactory1 = null;
        IDXGIAdapter1* pAdapter1 = null;
        IDXGIFactory4* pFactory4 = null;
        try {
            if (DxgiFactory->EnumAdapters(0u, &pAdapter) >= 0)
                return pAdapter;

            fixed (Guid* g = &IID.IID_IDXGIFactory1)
                DirectX.CreateDXGIFactory(g, (void**) &pFactory1).Ensure();

            pFactory1->EnumAdapters1(0u, &pAdapter1).Ensure();

            fixed (Guid* g = &IID.IID_IDXGIAdapter1)
                if (pAdapter1->QueryInterface(g, (void**) &pAdapter) >= 0)
                    return pAdapter;

            fixed (Guid* g = &IID.IID_IDXGIFactory4)
                DxgiFactory->QueryInterface(g, (void**) &pFactory4).Ensure();

            fixed (Guid* g = &IID.IID_IDXGIAdapter)
                pFactory4->EnumWarpAdapter(g, (void**) &pAdapter).Ensure();

            return pAdapter;
        } finally {
            SafeRelease(ref pFactory4);
            SafeRelease(ref pAdapter1);
            SafeRelease(ref pFactory1);
        }
    }

    protected static void SafeRelease<T>(ref T* u) where T : unmanaged
    {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
    }

    protected sealed class Direct3DDeviceBuilder : IDisposable {
        private readonly bool _disposeOnException;

        private D3D_FEATURE_LEVEL _minimumFeatureLevel = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1;
        private D3D_DRIVER_TYPE _driverType;
        private D3D11_CREATE_DEVICE_FLAG _flags;

        // in, refcounted
        private IDXGIAdapter* _pAdapter;

        // out
        private D3D_FEATURE_LEVEL _obtainedFeatureLevel = 0;

        // out, refcounted
        private ID3D11Device* _pDevice;
        private ID3D11DeviceContext* _pContext;

        private Direct3DDeviceBuilder(bool disposeOnException)
        {
            this._disposeOnException = disposeOnException;
        }

        public static Direct3DDeviceBuilder ManualDispose() => new(false);
        public static Direct3DDeviceBuilder DisposeOnException() => new(true);

        public void Dispose() => this.Clear();

        public Direct3DDeviceBuilder Clear()
        {
            SafeRelease(ref this._pAdapter);
            SafeRelease(ref this._pDevice);
            SafeRelease(ref this._pContext);
            this._obtainedFeatureLevel = 0;
            return this;
        }

        public Direct3DDeviceBuilder WithMinimumFeatureLevel(D3D_FEATURE_LEVEL minimumFeatureLevel)
        {
            this._minimumFeatureLevel = minimumFeatureLevel;
            return this;
        }

        public Direct3DDeviceBuilder WithDriverType(D3D_DRIVER_TYPE driverType)
        {
            try {
                SafeRelease(ref this._pAdapter);
                this._driverType = driverType;
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithAdapterTakeOwnership(IDXGIAdapter* pAdapter)
        {
            try {
                SafeRelease(ref this._pAdapter);
                this._driverType = D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN;
                this._pAdapter = pAdapter;
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithAdapterCopy(IDXGIAdapter* pAdapter)
        {
            try {
                SafeRelease(ref this._pAdapter);
                this._driverType = D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN;
                this._pAdapter = pAdapter;
                this._pAdapter->AddRef();
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithFlagReplace(D3D11_CREATE_DEVICE_FLAG flags)
        {
            this._flags = flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagAdd(D3D11_CREATE_DEVICE_FLAG flags)
        {
            this._flags |= flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagRemove(D3D11_CREATE_DEVICE_FLAG flags)
        {
            this._flags &= ~flags;
            return this;
        }

        public Direct3DDeviceBuilder Create()
        {
            try {
                var levels = new[] {
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
                    D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1,
                }.TakeWhile(x => x >= this._minimumFeatureLevel).ToArray();

                SafeRelease(ref this._pDevice);
                SafeRelease(ref this._pContext);
                fixed (ID3D11Device** ppD3dDevice = &this._pDevice)
                fixed (D3D_FEATURE_LEVEL* pFeatureLevel = &this._obtainedFeatureLevel)
                fixed (ID3D11DeviceContext** ppD3dContext = &this._pContext)
                fixed (D3D_FEATURE_LEVEL* pLevels = levels) {
                    DirectX.D3D11CreateDevice(
                        this._pAdapter,
                        this._driverType,
                        default,
                        (uint) this._flags,
                        pLevels,
                        (uint) levels.Length,
                        D3D11.D3D11_SDK_VERSION,
                        ppD3dDevice,
                        pFeatureLevel,
                        ppD3dContext).Ensure();
                }

                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDevice(out ComPtr<ID3D11Device> pDevice)
        {
            try {
                if (this._pDevice is null)
                    throw new NullReferenceException();
                pDevice = new(this._pDevice);
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeContext(out ComPtr<ID3D11DeviceContext> pContext)
        {
            try {
                if (this._pDevice is null)
                    throw new NullReferenceException();
                pContext = new(this._pContext);
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeContext<T>(out T* pContext, Guid typeGuid = default) where T : unmanaged
        {
            try {
                if (this._pContext is null)
                    throw new NullReferenceException();
                if (typeGuid == default) {
                    if (typeof(T).GetField("Guid", BindingFlags.Public | BindingFlags.Static) is not { } fieldInfo)
                        throw new ArgumentException($@"{typeof(T).Name} has no static field named Guid.", nameof(T));
                    if (fieldInfo.GetValue(null) is not Guid guid || guid == default)
                        throw new ArgumentException($@"{typeof(T).Name} has Guid field that is empty.", nameof(T));
                    typeGuid = guid;
                }

                fixed (void* ppContext = &pContext)
                    this._pContext->QueryInterface(&typeGuid, (void**) ppContext).Ensure();
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDeviceLevel(out D3D_FEATURE_LEVEL featureLevel)
        {
            try {
                if (this._obtainedFeatureLevel == 0)
                    throw new NullReferenceException();
                featureLevel = this._obtainedFeatureLevel;
                return this;
            } catch (Exception) {
                if (this._disposeOnException) this.Dispose();
                throw;
            }
        }
    }
}
