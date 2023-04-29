using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public abstract unsafe class D3D11Resource : DirectXObject {
    private ID3D11Resource* _pResource;

    public ID3D11Resource* Resource => _pResource;

    protected void SetResource<T>(T* value) where T : unmanaged {
        if (_pResource == value)
            return;

        ID3D11Resource* pResource = null;
        fixed (Guid* pGuid = &ID3D11Resource.Guid)
            ThrowH(((IUnknown*) value)->QueryInterface(pGuid, (void**) &pResource));
        SafeRelease(ref _pResource);
        _pResource = pResource;
    }

    protected override void Dispose(bool disposing) {
        SafeRelease(ref _pResource);
        base.Dispose(disposing);
    }
}
