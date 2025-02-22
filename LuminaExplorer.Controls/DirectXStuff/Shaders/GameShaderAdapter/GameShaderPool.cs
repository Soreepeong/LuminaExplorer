using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public sealed unsafe class GameShaderPool : DirectXObject {
    private readonly ID3D11SamplerState*[] _pSamplers;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private Texture2DShaderResource _dummy;

    public GameShaderPool(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
    {
        try {
            this._pDevice = pDevice;
            this._pDevice->AddRef();
            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();

            var samplerDesc = new D3D11_SAMPLER_DESC {
                Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                MaxAnisotropy = 0,
                AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                MipLODBias = 0f,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
                ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
            };
            fixed (ID3D11SamplerState** ppSamplers = this._pSamplers = new ID3D11SamplerState*[16]) {
                for (var i = 0; i < this._pSamplers.Length; i++)
                    pDevice->CreateSamplerState(&samplerDesc, ppSamplers + i).Ensure();
            }

            // Some materials refer to dummy.tex; make them point to this.
            fixed (float* pDummy = stackalloc float[16])
                this._dummy = new(this._pDevice, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, 4, 4, 16, (nint) (&pDummy));
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~GameShaderPool() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        for (var i = 0; i < this._pSamplers.Length; i++)
            SafeRelease(ref this._pSamplers[i]);
        SafeRelease(ref this._pDevice);
        SafeRelease(ref this._pDeviceContext);
    }

    private void DisposePrivate(bool disposing)
    {
        if (disposing)
            SafeDispose.One(ref this._dummy!);
        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public event ShaderEvents.FileRequested<ShpkFile>? ShpkFileRequested;

    public void SetSamplers()
    {
        fixed (ID3D11SamplerState** ppSamplers =
                   this._pSamplers) this._pDeviceContext->PSSetSamplers(0, (uint) this._pSamplers.Length, ppSamplers);
    }

    public void CopyDeviceAndContext(out ID3D11Device* pDevice, out ID3D11DeviceContext* pDeviceContext)
    {
        pDevice = this._pDevice;
        pDevice->AddRef();
        pDeviceContext = this._pDeviceContext;
        pDeviceContext->AddRef();
    }

    public void SetShaderResourcesToDummyTexture(uint slot, uint count)
    {
        Span<nint> r = stackalloc nint[(int) count];
        r.Fill((nint) this._dummy.ShaderResourceView);
        fixed (void* p = r) this._pDeviceContext->PSSetShaderResources(slot, count, (ID3D11ShaderResourceView**) p);
    }

    public Task<ShaderSet?>? GetShaderSet(MdlFile mdl, Material material)
    {
        Task<ShpkFile?>? task = null;
        this.ShpkFileRequested?.Invoke($"shader/sm5/shpk/{material.ShaderPack}", ref task);
        return task?.ContinueWith(
            r => {
                var shpk = r.Result;
                if (shpk is null)
                    return null;

                var mtrl = material;
                var key = shpk.MaterialKeys.Select(x => x.DefaultValue).ToArray();
                foreach (var k in mtrl.File!.ShaderKeys) {
                    for (var i = 0; i < shpk.MaterialKeys.Length; i++)
                        if (shpk.MaterialKeys[i].Id == k.Category)
                            key[i] = k.Value;
                }

                var candidates = shpk.Nodes.Where(x => x.MaterialKeys.SequenceEqual(key)).ToArray();

                Debugger.Break();

                return new ShaderSet(null!, null!);
            });
    }
}
