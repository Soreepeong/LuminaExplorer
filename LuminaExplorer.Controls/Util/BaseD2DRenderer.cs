using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using IDWriteFactory = Silk.NET.DirectWrite.IDWriteFactory;

namespace LuminaExplorer.Controls.Util;

public abstract unsafe class BaseD2DRenderer : IDisposable {
    private static Exception? _apiInitializationException;

    private static DXGI? _dxgiApi;
    private static D3D11? _d3d11Api;
    private static D2D? _d2dApi;
    private static DWrite? _dwriteApi;

    private static ID2D1Factory* _pD2D1Factory;
    private static IDWriteFactory* _pDWriteFactory;
    private static IDXGIFactory* _pDxgiFactory;

    private static ID3D11Device* _pSharedD3D11Device;
    private static ID3D11DeviceContext* _pSharedD3D11Context;

    protected static void TryInitializeApis() {
        if (_apiInitializationException is not null)
            throw _apiInitializationException;

        try {
            _d2dApi = D2D.GetApi();
            _d3d11Api = D3D11.GetApi(new NullNativeWindowSource());
            _dxgiApi = DXGI.GetApi(new NullNativeWindowSource());
            _dwriteApi = DWrite.GetApi();

            fixed (void* ppFactory = &_pDxgiFactory)
            fixed (Guid* g = &IDXGIFactory.Guid)
                ThrowH(Dxgi.CreateDXGIFactory(g, (void**) ppFactory));

            fixed (void* ppFactory = &_pD2D1Factory)
            fixed (Guid* g = &ID2D1Factory.Guid) {
                var fo = new FactoryOptions();
                ThrowH(D2D.D2D1CreateFactory(
                    Silk.NET.Direct2D.FactoryType.SingleThreaded, g, &fo, (void**) ppFactory));
            }

            fixed (void* ppFactory = &_pDWriteFactory)
            fixed (Guid* g = &IDWriteFactory.Guid) {
                ThrowH(_dwriteApi.DWriteCreateFactory(
                    Silk.NET.DirectWrite.FactoryType.Isolated, g, (IUnknown**) ppFactory));
            }

            Direct3DDeviceBuilder
                .DisposeOnException()
                .WithAdapterTakeOwnership(GetAnyAvailableDxgiAdapter())
                .WithFlagAdd(CreateDeviceFlag.Debug)
                .WithFlagAdd(CreateDeviceFlag.BgraSupport)
                .Create()
                .TakeDevice(out _pSharedD3D11Device)
                .TakeContext(out _pSharedD3D11Context)
                .Dispose();

            using var dxgiDevice = SharedD3D11Device->QueryInterface<IDXGIDevice2>();
            dxgiDevice.SetMaximumFrameLatency(1);
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

    protected static DXGI Dxgi => _dxgiApi ?? throw InitializationException;
    protected static D3D11 D3D11 => _d3d11Api ?? throw InitializationException;
    protected static D2D D2D => _d2dApi ?? throw InitializationException;
    protected static DWrite DWrite => _dwriteApi ?? throw InitializationException;

    protected static IDXGIFactory* DxgiFactory => _pD2D1Factory is not null
        ? _pDxgiFactory
        : throw InitializationException;

    protected static ID2D1Factory* D2DFactory => _pD2D1Factory is not null
        ? _pD2D1Factory
        : throw InitializationException;

    protected static IDWriteFactory* DWriteFactory => _pDWriteFactory is not null
        ? _pDWriteFactory
        : throw InitializationException;

    protected static ID3D11Device* SharedD3D11Device => _pSharedD3D11Device is not null
        ? _pSharedD3D11Device
        : throw InitializationException;

    protected static ID3D11DeviceContext* SharedD3D11Context => _pSharedD3D11Context is not null
        ? _pSharedD3D11Context
        : throw InitializationException;

    private static Exception InitializationException => _apiInitializationException ?? new Exception("Uninitialized");

    protected static IDXGIAdapter* GetAnyAvailableDxgiAdapter() {
        IDXGIAdapter* pAdapter = null;
        IDXGIFactory1* pFactory1 = null;
        IDXGIAdapter1* pAdapter1 = null;
        IDXGIFactory4* pFactory4 = null;
        try {
            if (DxgiFactory->EnumAdapters(0u, &pAdapter) >= 0)
                return pAdapter;

            fixed (Guid* g = &IDXGIFactory1.Guid)
                ThrowH(Dxgi.CreateDXGIFactory(g, (void**) &pFactory1));

            ThrowH(pFactory1->EnumAdapters1(0u, &pAdapter1));

            fixed (Guid* g = &IDXGIAdapter1.Guid)
                if (pAdapter1->QueryInterface(g, (void**) &pAdapter) >= 0)
                    return pAdapter;

            fixed (Guid* g = &IDXGIFactory4.Guid)
                ThrowH(DxgiFactory->QueryInterface(g, (void**) &pFactory4));

            fixed (Guid* g = &IDXGIAdapter.Guid)
                ThrowH(pFactory4->EnumWarpAdapter(g, (void**) &pAdapter));

            return pAdapter;
        } finally {
            SafeRelease(ref pFactory4);
            SafeRelease(ref pAdapter1);
            SafeRelease(ref pFactory1);
        }
    }

    protected static void ThrowH(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    protected static void SafeRelease<T>(ref T* u) where T : unmanaged {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
    }

    protected static void SafeReleaseArray<T>(ref T*[]?[]?[]? array) where T : unmanaged {
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            SafeReleaseArray(ref array[i]);
        array = null;
    }

    protected static void SafeReleaseArray<T>(ref T*[]?[]? array) where T : unmanaged {
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            SafeReleaseArray(ref array[i]);
        array = null;
    }

    protected static void SafeReleaseArray<T>(ref T*[]? array) where T : unmanaged {
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            SafeRelease(ref array[i]);
        array = null;
    }

    protected static void SafeReleaseArray<T>(ref ComPtr<T>[]?[]?[]? array) where T : unmanaged , IComVtbl<T>{
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            SafeReleaseArray(ref array[i]);
        array = null;
    }

    protected static void SafeReleaseArray<T>(ref ComPtr<T>[]?[]? array) where T : unmanaged , IComVtbl<T>{
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            SafeReleaseArray(ref array[i]);
        array = null;
    }

    protected static void SafeReleaseArray<T>(ref ComPtr<T>[]? array) where T : unmanaged, IComVtbl<T> {
        if (array is null)
            return;
        for (var i = 0; i < array.Length; i++)
            array[i].Release();
        array = null;
    }

    protected sealed class Direct3DDeviceBuilder : IDisposable {
        private readonly bool _disposeOnException;

        private D3DFeatureLevel _minimumFeatureLevel = D3DFeatureLevel.Level91;
        private D3DDriverType _driverType;
        private CreateDeviceFlag _flags;

        // in, refcounted
        private IDXGIAdapter* _pAdapter;

        // out
        private D3DFeatureLevel _obtainedFeatureLevel = 0;

        // out, refcounted
        private ID3D11Device* _pDevice;
        private ID3D11DeviceContext* _pContext;

        private Direct3DDeviceBuilder(bool disposeOnException) {
            _disposeOnException = disposeOnException;
        }

        public static Direct3DDeviceBuilder ManualDispose() => new(false);
        public static Direct3DDeviceBuilder DisposeOnException() => new(true);

        public void Dispose() => Clear();

        public Direct3DDeviceBuilder Clear() {
            SafeRelease(ref _pAdapter);
            SafeRelease(ref _pDevice);
            SafeRelease(ref _pContext);
            _obtainedFeatureLevel = 0;
            return this;
        }

        public Direct3DDeviceBuilder WithMinimumFeatureLevel(D3DFeatureLevel minimumFeatureLevel) {
            _minimumFeatureLevel = minimumFeatureLevel;
            return this;
        }

        public Direct3DDeviceBuilder WithDriverType(D3DDriverType driverType) {
            try {
                SafeRelease(ref _pAdapter);
                _driverType = driverType;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithAdapterTakeOwnership(IDXGIAdapter* pAdapter) {
            try {
                SafeRelease(ref _pAdapter);
                _driverType = D3DDriverType.Unknown;
                _pAdapter = pAdapter;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithAdapterCopy(IDXGIAdapter* pAdapter) {
            try {
                SafeRelease(ref _pAdapter);
                _driverType = D3DDriverType.Unknown;
                _pAdapter = pAdapter;
                _pAdapter->AddRef();
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder WithFlagReplace(CreateDeviceFlag flags) {
            _flags = flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagAdd(CreateDeviceFlag flags) {
            _flags |= flags;
            return this;
        }

        public Direct3DDeviceBuilder WithFlagRemove(CreateDeviceFlag flags) {
            _flags &= ~flags;
            return this;
        }

        public Direct3DDeviceBuilder Create() {
            try {
                var levels = new[] {
                    D3DFeatureLevel.Level111,
                    D3DFeatureLevel.Level110,
                    D3DFeatureLevel.Level101,
                    D3DFeatureLevel.Level100,
                    D3DFeatureLevel.Level93,
                    D3DFeatureLevel.Level92,
                    D3DFeatureLevel.Level91,
                }.TakeWhile(x => x >= _minimumFeatureLevel).ToArray();

                SafeRelease(ref _pDevice);
                SafeRelease(ref _pContext);
                fixed (ID3D11Device** ppD3dDevice = &_pDevice)
                fixed (D3DFeatureLevel* pFeatureLevel = &_obtainedFeatureLevel)
                fixed (ID3D11DeviceContext** ppD3dContext = &_pContext)
                fixed (D3DFeatureLevel* pLevels = levels)
                    ThrowH(D3D11.CreateDevice(
                        _pAdapter,
                        _driverType,
                        nint.Zero,
                        (uint) _flags,
                        pLevels,
                        (uint) levels.Length,
                        D3D11.SdkVersion,
                        ppD3dDevice,
                        pFeatureLevel,
                        ppD3dContext));
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDevice(out ID3D11Device* pDevice) {
            try {
                if (_pDevice is null)
                    throw new NullReferenceException();
                pDevice = _pDevice;
                pDevice->AddRef();
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDevice<T>(out T* pDevice, Guid typeGuid = default) where T : unmanaged {
            try {
                if (_pDevice is null)
                    throw new NullReferenceException();
                if (typeGuid == default) {
                    if (typeof(T).GetField("Guid", BindingFlags.Public | BindingFlags.Static) is not { } fieldInfo)
                        throw new ArgumentException($@"{typeof(T).Name} has no static field named Guid.", nameof(T));
                    if (fieldInfo.GetValue(null) is not Guid guid || guid == default)
                        throw new ArgumentException($@"{typeof(T).Name} has Guid field that is empty.", nameof(T));
                    typeGuid = guid;
                }

                fixed (void* ppDevice = &pDevice)
                    ThrowH(_pDevice->QueryInterface(&typeGuid, (void**) ppDevice));
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeContext(out ID3D11DeviceContext* pContext) {
            try {
                if (_pDevice is null)
                    throw new NullReferenceException();
                pContext = _pContext;
                pContext->AddRef();
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeContext<T>(out T* pContext, Guid typeGuid = default) where T : unmanaged {
            try {
                if (_pContext is null)
                    throw new NullReferenceException();
                if (typeGuid == default) {
                    if (typeof(T).GetField("Guid", BindingFlags.Public | BindingFlags.Static) is not { } fieldInfo)
                        throw new ArgumentException($@"{typeof(T).Name} has no static field named Guid.", nameof(T));
                    if (fieldInfo.GetValue(null) is not Guid guid || guid == default)
                        throw new ArgumentException($@"{typeof(T).Name} has Guid field that is empty.", nameof(T));
                    typeGuid = guid;
                }

                fixed (void* ppContext = &pContext)
                    ThrowH(_pContext->QueryInterface(&typeGuid, (void**) ppContext));
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }

        public Direct3DDeviceBuilder TakeDeviceLevel(out D3DFeatureLevel featureLevel) {
            try {
                if (_obtainedFeatureLevel == 0)
                    throw new NullReferenceException();
                featureLevel = _obtainedFeatureLevel;
                return this;
            } catch (Exception) {
                if (_disposeOnException)
                    Dispose();
                throw;
            }
        }
    }

    private class NullNativeWindowSource : INativeWindowSource {
        public INativeWindow? Native => null;
    }
}
