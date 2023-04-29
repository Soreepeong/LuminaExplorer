using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class DepthStencilResource : D3D11Resource {
    private ID3D11Texture2D* _pDepthStencil = null;
    private ID3D11DepthStencilView* _pDepthStencilView = null;

    public DepthStencilResource(ID3D11Device* pDevice, IUnknown* pBackBuffer) {
        ID3D11Texture2D* pBackBufferTexture2D = null;
        fixed (Guid* pGuid = &ID3D11Texture2D.Guid)
            ThrowH(pBackBuffer->QueryInterface(pGuid, (void**) &pBackBufferTexture2D));

        try {
            var backBufferDesc = new Texture2DDesc();
            pBackBufferTexture2D->GetDesc(&backBufferDesc);

            var depthStencilDesc = new Texture2DDesc(
                width: backBufferDesc.Width,
                height: backBufferDesc.Height,
                mipLevels: 1,
                arraySize: 1,
                format: Format.FormatD24UnormS8Uint,
                sampleDesc: new(1, 0),
                usage: Usage.Default,
                bindFlags: (uint) BindFlag.DepthStencil,
                cPUAccessFlags: 0,
                miscFlags: 0);

            fixed (ID3D11Texture2D** ppDepthStencil = &_pDepthStencil)
                ThrowH(pDevice->CreateTexture2D(&depthStencilDesc, null, ppDepthStencil));
            SetResource(_pDepthStencil);

            var depthStencilViewDesc = new DepthStencilViewDesc(
                format: depthStencilDesc.Format,
                viewDimension: DsvDimension.Texture2D,
                flags: 0,
                texture2D: new(0));
            fixed (ID3D11DepthStencilView** ppDepthStencilView = &_pDepthStencilView)
                ThrowH(pDevice->CreateDepthStencilView(Resource, &depthStencilViewDesc, ppDepthStencilView));
        } finally {
            pBackBufferTexture2D->Release();
        }
    }

    public ID3D11DepthStencilView* View => _pDepthStencilView;

    private void DisposeInner() {
        SafeRelease(ref _pDepthStencil);
        SafeRelease(ref _pDepthStencilView);
    }

    protected override void Dispose(bool disposing) {
        DisposeInner();
        base.Dispose(disposing);
    }
}
