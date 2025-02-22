using System;
using System.Runtime.CompilerServices;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class ConstantBufferResource<T> : D3D11Resource where T : unmanaged {
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11Buffer* _pBuffer;

    public ConstantBufferResource(
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext,
        bool initialEnablePullState = true,
        T? initialData = null)
    {
        try {
            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();
            fixed (ID3D11Buffer** ppBuffer = &this._pBuffer) {
                var bufferDesc = new D3D11_BUFFER_DESC(
                    byteWidth: (uint) ((Unsafe.SizeOf<T>() + 15) / 16 * 16),
                    bindFlags: (uint) D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
                if (initialData is not null) {
                    var data = initialData.Value;
                    var subr = new D3D11_SUBRESOURCE_DATA { pSysMem = &data };
                    pDevice->CreateBuffer(&bufferDesc, &subr, ppBuffer).Ensure();
                } else
                    pDevice->CreateBuffer(&bufferDesc, null, ppBuffer).Ensure();
            }

            this.SetResource(this._pBuffer);

            this.EnablePull = initialEnablePullState;
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~ConstantBufferResource() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        SafeRelease(ref this._pBuffer);
        SafeRelease(ref this._pDeviceContext);
    }

    private void DisposePrivate(bool disposing)
    {
        _ = disposing;
        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(true);
        base.Dispose(disposing);
    }

    protected ID3D11DeviceContext* DeviceContext => this._pDeviceContext;

    private bool _pendingDataAvailable;
    private T _pendingData;

    public event DataPullDelegate? DataPull;

    public ID3D11Buffer* Buffer {
        get {
            if (this.EnablePull) this.DataPull?.Invoke(this);
            if (this._pendingDataAvailable) {
                this.UpdateDataOnce(this._pendingData);
                this._pendingDataAvailable = false;
            }

            return this._pBuffer;
        }
    }

    public bool EnablePull { get; set; }

    public void UpdateData(T data)
    {
        this.DeviceContext->UpdateSubresource(this.Resource, 0, null, &data, 0, 0);
        this._pendingDataAvailable = false;
        this.EnablePull = false;
    }

    public void UpdateDataLater(T data)
    {
        this._pendingData = data;
        this._pendingDataAvailable = true;
    }

    public void UpdateDataOnce(T data)
    {
        this.DeviceContext->UpdateSubresource(this.Resource, 0, null, &data, 0, 0);
        this._pendingDataAvailable = false;
    }

    public delegate void DataPullDelegate(ConstantBufferResource<T> sender);
}
