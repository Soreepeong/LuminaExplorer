using System;
using System.Runtime.CompilerServices;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public sealed unsafe class ConstantBufferResource<T> : D3D11Resource where T : unmanaged {
    private ID3D11DeviceContext* _pContext;
    private ID3D11Buffer* _buffer;

    public ConstantBufferResource(ID3D11Device* pDevice, ID3D11DeviceContext* pContext) {
        try {
            _pContext = pContext;
            _pContext->AddRef();
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

    public ID3D11Buffer* Buffer => _buffer;

    public bool UpdateRequired { get; private set; } = true;

    public void MarkUpdateRequired() => UpdateRequired = true;

    public void UpdateData(T data) {
        _pContext->UpdateSubresource(Resource, 0, null, &data, 0, 0);
        UpdateRequired = false;
    }

    protected override void Dispose(bool disposing) {
        SafeRelease(ref _buffer);
        SafeRelease(ref _pContext);
        base.Dispose(disposing);
    }
}
