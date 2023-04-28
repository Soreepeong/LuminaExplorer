using System;
using System.Runtime.CompilerServices;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff;

public sealed unsafe class ConstantBufferResource<T> : D3D11Resource where T : unmanaged {
    private ulong _dataVersion = 0;
    private ID3D11Buffer* _buffer;

    public ConstantBufferResource(ID3D11Device* pDevice) {
        try {
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

    public ulong DataVersion => _dataVersion;

    public ID3D11Buffer* Buffer => _buffer;

    public void UpdateData(ID3D11DeviceContext* pContext, ulong dataVersion, T data) {
        if (dataVersion == _dataVersion)
            return;
        
        pContext->UpdateSubresource(Resource, 0, null, &data, 0, 0);
        _dataVersion = dataVersion;
    }

    protected override void Dispose(bool disposing) {
        SafeRelease(ref _buffer);
        base.Dispose(disposing);
    }
}
