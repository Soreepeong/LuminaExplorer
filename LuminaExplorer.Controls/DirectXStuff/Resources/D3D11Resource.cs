using System;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public abstract unsafe class D3D11Resource : DirectXObject {
    private ID3D11Resource* _pResource;

    public ID3D11Resource* Resource => this._pResource;

    protected void SetResource<T>(T* value) where T : unmanaged, IUnknown.Interface
    {
        if (this._pResource == value)
            return;

        ID3D11Resource* pResource = null;
        fixed (Guid* pGuid = &IID.IID_ID3D11Resource)
            ((IUnknown*) value)->QueryInterface(pGuid, (void**) &pResource).Ensure();
        SafeRelease(ref this._pResource);
        this._pResource = pResource;
    }

    protected override void Dispose(bool disposing)
    {
        SafeRelease(ref this._pResource);
        base.Dispose(disposing);
    }
}
