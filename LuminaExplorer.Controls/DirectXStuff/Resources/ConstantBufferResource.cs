using System;
using System.Runtime.CompilerServices;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class ConstantBufferResource<T> : D3D11Resource where T : unmanaged {
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11Buffer* _pBuffer;

    public ConstantBufferResource(
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext,
        bool initialEnablePullState = true,
        T? initialData = null) {
        try {
            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();
            fixed (ID3D11Buffer** ppBuffer = &_pBuffer) {
                var bufferDesc = new BufferDesc(
                    byteWidth: (uint) ((Unsafe.SizeOf<T>() + 15) / 16 * 16),
                    usage: Usage.Default,
                    bindFlags: (uint) BindFlag.ConstantBuffer,
                    cPUAccessFlags: 0,
                    miscFlags: 0,
                    structureByteStride: 0);
                if (initialData is not null) {
                    var data = initialData.Value;
                    var subr = new SubresourceData(pSysMem: &data);
                    ThrowH(pDevice->CreateBuffer(&bufferDesc, &subr, ppBuffer));
                } else
                    ThrowH(pDevice->CreateBuffer(&bufferDesc, null, ppBuffer));
            }

            SetResource(_pBuffer);
            
            EnablePull = initialEnablePullState;
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    ~ConstantBufferResource() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        SafeRelease(ref _pBuffer);
        SafeRelease(ref _pDeviceContext);
    }

    private void DisposePrivate(bool disposing) {
        _ = disposing;
        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(true);
        base.Dispose(disposing);
    }

    protected ID3D11DeviceContext* DeviceContext => _pDeviceContext;

    private bool _pendingDataAvailable;
    private T _pendingData;

    public event DataPullDelegate? DataPull;

    public ID3D11Buffer* Buffer {
        get {
            if (EnablePull)
                DataPull?.Invoke(this);
            if (_pendingDataAvailable) {
                UpdateDataOnce(_pendingData);
                _pendingDataAvailable = false;
            }

            return _pBuffer;
        }
    }

    public bool EnablePull { get; set; }

    public void UpdateData(T data) {
        DeviceContext->UpdateSubresource(Resource, 0, null, &data, 0, 0);
        _pendingDataAvailable = false;
        EnablePull = false;
    }

    public void UpdateDataLater(T data) {
        _pendingData = data;
        _pendingDataAvailable = true;
    }

    public void UpdateDataOnce(T data) {
        DeviceContext->UpdateSubresource(Resource, 0, null, &data, 0, 0);
        _pendingDataAvailable = false;
    }

    public delegate void DataPullDelegate(ConstantBufferResource<T> sender);
}
