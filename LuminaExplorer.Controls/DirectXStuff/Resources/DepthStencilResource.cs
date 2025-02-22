using System;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class DepthStencilResource : D3D11Resource {
    private ID3D11Texture2D* _pDepthStencil = null;
    private ID3D11DepthStencilView* _pDepthStencilView = null;

    public DepthStencilResource(ID3D11Device* pDevice, IUnknown* pBackBuffer)
    {
        ID3D11Texture2D* pBackBufferTexture2D = null;
        fixed (Guid* pGuid = &IID.IID_ID3D11Texture2D)
            pBackBuffer->QueryInterface(pGuid, (void**) &pBackBufferTexture2D).Ensure();

        try {
            var backBufferDesc = new D3D11_TEXTURE2D_DESC();
            pBackBufferTexture2D->GetDesc(&backBufferDesc);

            var depthStencilDesc = new D3D11_TEXTURE2D_DESC(
                format: DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT,
                width: backBufferDesc.Width,
                height: backBufferDesc.Height,
                mipLevels: 1,
                bindFlags: (uint) D3D11_BIND_FLAG.D3D11_BIND_DEPTH_STENCIL);

            fixed (ID3D11Texture2D** ppDepthStencil = &this._pDepthStencil)
                pDevice->CreateTexture2D(&depthStencilDesc, null, ppDepthStencil).Ensure();
            this.SetResource(this._pDepthStencil);

            var depthStencilViewDesc = new D3D11_DEPTH_STENCIL_VIEW_DESC(
                format: depthStencilDesc.Format,
                viewDimension: D3D11_DSV_DIMENSION.D3D11_DSV_DIMENSION_TEXTURE2D);
            fixed (ID3D11DepthStencilView** ppDepthStencilView = &this._pDepthStencilView)
                pDevice->CreateDepthStencilView(this.Resource, &depthStencilViewDesc, ppDepthStencilView).Ensure();
        } finally {
            pBackBufferTexture2D->Release();
        }
    }

    public ID3D11DepthStencilView* View => this._pDepthStencilView;

    private void DisposeInner()
    {
        SafeRelease(ref this._pDepthStencil);
        SafeRelease(ref this._pDepthStencilView);
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposeInner();
        base.Dispose(disposing);
    }
}
