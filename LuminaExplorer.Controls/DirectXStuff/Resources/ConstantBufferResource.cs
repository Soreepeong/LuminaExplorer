using System;
using System.Runtime.CompilerServices;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public sealed unsafe class ConstantBufferResource<T> : D3D11Resource where T : unmanaged {
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11Buffer* _buffer;

    public ConstantBufferResource(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext) {
        try {
            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();
            fixed (ID3D11Buffer** ppBuffer = &_buffer) {
                var bufferDesc = new BufferDesc(
                    byteWidth: (uint) ((Unsafe.SizeOf<T>() + 15) / 16 * 16),
                    usage: Usage.Default,
                    bindFlags: (uint) BindFlag.ConstantBuffer,
                    cPUAccessFlags: 0,
                    miscFlags: 0,
                    structureByteStride: 0);
                ThrowH(pDevice->CreateBuffer(&bufferDesc, null, ppBuffer));
            }

            SetResource(_buffer);
        } catch (Exception) {
            Dispose();
            throw;
        }
    }
        
    ~ConstantBufferResource() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        SafeRelease(ref _buffer);
        SafeRelease(ref _pDeviceContext);
    }

    protected override void Dispose(bool disposing) {
        ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }

    public ID3D11Buffer* Buffer => _buffer;

    public bool UpdateRequired { get; private set; } = true;

    public void MarkUpdateRequired() => UpdateRequired = true;

    public void UpdateData(T data) {
        _pDeviceContext->UpdateSubresource(Resource, 0, null, &data, 0, 0);
        UpdateRequired = false;
    }
}
