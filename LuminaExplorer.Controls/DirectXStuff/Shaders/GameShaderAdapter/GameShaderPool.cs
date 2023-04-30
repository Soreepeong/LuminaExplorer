using System;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public sealed unsafe class GameShaderPool : DirectXObject {
    private readonly ID3D11SamplerState*[] _pSamplers;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private Texture2DShaderResource _dummy;

    public GameShaderPool(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext) {
        try {
            _pDevice = pDevice;
            _pDevice->AddRef();
            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();

            var samplerDesc = new SamplerDesc {
                Filter = Filter.MinMagMipLinear,
                MaxAnisotropy = 0,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MipLODBias = 0f,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
                ComparisonFunc = ComparisonFunc.Never,
            };
            fixed (ID3D11SamplerState** ppSamplers = _pSamplers = new ID3D11SamplerState*[16]) {
                for (var i = 0; i < _pSamplers.Length; i++)
                    ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + i));
            }

            // Some materials refer to dummy.tex; make them point to this.
            fixed (float* pDummy = stackalloc float[16])
                _dummy = new(_pDevice, Format.FormatR8G8B8A8Unorm, 4, 4, 16, (nint) (&pDummy));
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    ~GameShaderPool() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        for (var i = 0; i < _pSamplers.Length; i++)
            SafeRelease(ref _pSamplers[i]);
        SafeRelease(ref _pDevice);
        SafeRelease(ref _pDeviceContext);
    }

    private void DisposePrivate(bool disposing) {
        if (disposing)
            SafeDispose.One(ref _dummy!);
        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public void SetSamplers() {
        fixed (ID3D11SamplerState** ppSamplers = _pSamplers)
            _pDeviceContext->PSSetSamplers(0, (uint) _pSamplers.Length, ppSamplers);
    }

    public void CopyDeviceAndContext(out ID3D11Device* pDevice, out ID3D11DeviceContext* pDeviceContext) {
        pDevice = _pDevice;
        pDevice->AddRef();
        pDeviceContext = _pDeviceContext;
        pDeviceContext->AddRef();
    }

    public void SetShaderResourcesToDummyTexture(uint slot, uint count) {
        Span<nint> r = stackalloc nint[(int) count];
        r.Fill((nint) _dummy.ShaderResourceView);
        fixed (void* p = r)
            _pDeviceContext->PSSetShaderResources(slot, count, (ID3D11ShaderResourceView**) p);
    }

    public Task<ShaderSet> GetShaderSet(MdlFile mdl, Material material) {
        var shpk = material.ShaderPack;
        throw new NotImplementedException();
    }
}
