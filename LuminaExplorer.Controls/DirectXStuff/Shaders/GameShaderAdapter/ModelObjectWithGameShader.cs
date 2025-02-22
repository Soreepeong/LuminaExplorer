using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using Lumina.Models.Materials;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class ModelObjectWithGameShader : DirectXObject {
    private readonly GameShaderPool _pool;
    private readonly MdlFile _mdl;
    private readonly int _variantId;
    private readonly int _lodIndex;
    private readonly Task<Material?>?[] _materials;
    private readonly Task<ShaderSet?>?[] _shaderSets;
    private readonly ID3D11InputLayout*[] _pInputLayouts;
    private readonly Task<Texture2DShaderResource?>?[ /* Material Index*/]?[ /* Texture Index */] _textures;
    private readonly ID3D11SamplerState*[ /* Material Index*/]?[ /* Texture Index */] _pSamplers;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private readonly ID3D11Buffer*[] _pIndexBuffers;
    private readonly ID3D11Buffer*[] _pVertexBuffers;

    public ModelObjectWithGameShader(
        GameShaderPool pool,
        MdlFile mdl,
        int variantId = 1,
        LodLevel lod = LodLevel.Highest)
    {
        Debug.Assert(
            mdl.Meshes.Length == mdl.VertexDeclarations.Length,
            "Mesh.ReadVertices seems to be expecting Meshes and VertexDeclarations to have same length.");

        try {
            // Ensure that we at least have non-null arrays in case of exceptions.
            this._materials = [];
            this._shaderSets = [];
            this._pInputLayouts = [];
            this._textures = [];
            this._pSamplers = [];
            this._pIndexBuffers = [];
            this._pVertexBuffers = [];

            this._pool = pool;
            this._pool.CopyDeviceAndContext(out this._pDevice, out this._pDeviceContext);

            this._mdl = mdl;
            this._variantId = variantId;
            this._lodIndex = (int) lod;
            this._materials = new Task<Material?>?[mdl.FileHeader.MaterialCount];
            this._shaderSets = new Task<ShaderSet?>?[mdl.FileHeader.MaterialCount];
            this._textures = new Task<Texture2DShaderResource?>[this._materials.Length][];
            this._pSamplers = new ID3D11SamplerState*[this._materials.Length][];

            this._pInputLayouts = new ID3D11InputLayout*[mdl.Meshes.Length];

            this._pIndexBuffers = new ID3D11Buffer*[this._mdl.FileHeader.LodCount];
            this._pVertexBuffers = new ID3D11Buffer*[this._mdl.FileHeader.LodCount];
            for (var i = 0; i < this._mdl.FileHeader.LodCount; i++) {
                fixed (void* pData = &this._mdl.Data[this._mdl.FileHeader.IndexOffset[i]])
                fixed (ID3D11Buffer** ppBuffer = &this._pIndexBuffers[i]) {
                    var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                    var desc = new D3D11_BUFFER_DESC(
                        this._mdl.FileHeader.IndexBufferSize[i],
                        (uint) D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);
                    this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
                }

                fixed (void* pData = &this._mdl.Data[this._mdl.FileHeader.VertexOffset[i]])
                fixed (ID3D11Buffer** ppBuffer = &this._pVertexBuffers[i]) {
                    var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                    var desc = new D3D11_BUFFER_DESC(
                        this._mdl.FileHeader.VertexBufferSize[i],
                        (uint) D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
                    this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
                }
            }
        } catch (Exception) {
            this.DisposeInner(true);
            throw;
        }
    }

    ~ModelObjectWithGameShader() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        for (var i = 0; i < this._pInputLayouts.Length; i++)
            SafeRelease(ref this._pInputLayouts[i]);
        for (var i = 0; i < this._pIndexBuffers.Length; i++)
            SafeRelease(ref this._pIndexBuffers[i]);
        for (var i = 0; i < this._pVertexBuffers.Length; i++)
            SafeRelease(ref this._pVertexBuffers[i]);
        foreach (var t in this._pSamplers) {
            if (t is not null) {
                for (var i = 0; i < t.Length; i++)
                    SafeRelease(ref t[i]);
            }
        }

        SafeRelease(ref this._pDeviceContext);
        SafeRelease(ref this._pDevice);
    }

    private void DisposeInner(bool disposing)
    {
        if (disposing) {
            foreach (var t in this._textures) {
                if (t is not null)
                    foreach (var j in t)
                        j?.Dispose();
            }
        }

        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposeInner(disposing);
        base.Dispose(disposing);
    }

    public event ShaderEvents.FileRequested<DdsFile>? DdsFileRequested;

    public event ShaderEvents.FileRequested<MtrlFile>? MtrlFileRequested;

    public event Action? ResourceLoadStateChanged;

    public void GetBuffers(LodLevel lod, out ID3D11Buffer* pVertexBuffer, out ID3D11Buffer* pIndexBuffer)
    {
        pVertexBuffer = this._pVertexBuffers[(int) lod];
        pIndexBuffer = this._pIndexBuffers[(int) lod];
    }

    public IEnumerable<MeshPart> Enumerate(
        int startMeshIndex,
        int meshCount)
    {
        for (var i = startMeshIndex; i < startMeshIndex + meshCount; i++) {
            var mesh = this._mdl.Meshes[i];

            if (mesh.SubMeshCount == 0) {
                yield return new(
                    i,
                    mesh.VertexBufferOffset[this._lodIndex],
                    mesh.VertexBufferStride[this._lodIndex],
                    mesh.StartIndex,
                    mesh.IndexCount);
            } else {
                foreach (var sm in this._mdl.Submeshes.Skip(mesh.SubMeshIndex).Take(mesh.SubMeshCount))
                    yield return new(
                        i,
                        mesh.VertexBufferOffset[this._lodIndex],
                        mesh.VertexBufferStride[this._lodIndex],
                        sm.IndexOffset,
                        sm.IndexCount);
            }
        }
    }

    public bool TryGetMaterialAndShader(
        int meshIndex,
        out int materialIndex,
        [MaybeNullWhen(false)] out Material material,
        [MaybeNullWhen(false)] out ShaderSet shaderSet,
        out ID3D11InputLayout* pInputLayout)
    {
        materialIndex = this._mdl.Meshes[meshIndex].MaterialIndex;
        material = null!;
        shaderSet = null!;
        pInputLayout = null;

        var materialTask = this._materials[materialIndex];
        if (materialTask is null) {
            if (this.MtrlFileRequested is null)
                return false;

            var mtrlPathSpan = this._mdl.Strings.AsSpan((int) this._mdl.MaterialNameOffsets[materialIndex]);
            mtrlPathSpan = mtrlPathSpan[..mtrlPathSpan.IndexOf((byte) 0)];

            var mtrlPath = Encoding.UTF8.GetString(mtrlPathSpan);
            if (mtrlPath.StartsWith('/')) {
                mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, this._variantId);
                if (mtrlPath is null) {
                    this._materials[materialIndex] = Task.FromResult((Material?) null);
                    return false;
                }
            }

            Task<MtrlFile?>? loader = null;
            this.MtrlFileRequested?.Invoke(mtrlPath, ref loader);
            if (loader is null)
                return false;

            var pDevice = this._pDevice;
            pDevice->AddRef();
            this._materials[materialIndex] = materialTask = loader.ContinueWith(
                r => {
                    try {
                        if (!r.IsCompletedSuccessfully || r.Result is not { } mtrlFile)
                            return null;
                        var m = new Material(mtrlFile);
                        var materialIndex = this._mdl.Meshes[meshIndex].MaterialIndex;

                        this._textures[materialIndex] = new Task<Texture2DShaderResource?>?[m.Textures.Length];
                        var samplers = this._pSamplers[materialIndex] = new ID3D11SamplerState*[m.Textures.Length];

                        var samplerDesc = new D3D11_SAMPLER_DESC(
                            filter: D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                            addressU: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                            addressV: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                            addressW: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                            mipLODBias: 0,
                            maxAnisotropy: 0,
                            comparisonFunc: D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                            borderColor: null,
                            minLOD: 0,
                            maxLOD: float.MaxValue);
                        for (var i = 0; i < mtrlFile.Samplers.Length; i++) {
                            fixed (ID3D11SamplerState** ppSampler = &samplers[i])
                                pDevice->CreateSamplerState(&samplerDesc, ppSampler).Ensure();
                        }

                        return m;
                    } finally {
                        pDevice->Release();
                    }
                });
            materialTask.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
        }

        if (materialTask is not { IsCompletedSuccessfully: true, Result: { } mat })
            return false;

        material = mat;

        if (this._shaderSets[materialIndex] == null) {
            var t = this._shaderSets[materialIndex] = this._pool.GetShaderSet(this._mdl, mat);
            if (t is null)
                return false;
            t.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
        }

        if (this._shaderSets[materialIndex] is not { IsCompletedSuccessfully: true, Result: { } set })
            return false;

        pInputLayout = this._pInputLayouts[meshIndex];
        if (pInputLayout is null) {
            pInputLayout = this._pInputLayouts[meshIndex] =
                shaderSet.Vs.GetInputLayout(this._mdl.VertexDeclarations[meshIndex]);
            pInputLayout->AddRef();
        }

        shaderSet = set;
        return true;
    }

    public bool TryGetTexture(int materialIndex, int textureIndex, out ID3D11ShaderResourceView* pTexture)
    {
        pTexture = null;
        if (this._materials[materialIndex] is not { IsCompletedSuccessfully: true, Result: { } mat })
            return false;

        if (this._textures[materialIndex] is not { } textures)
            return false;

        var task = textures[textureIndex];
        if (task is null) {
            var textureDefinition = mat.Textures[textureIndex];
            if (textureDefinition.TexturePath == "dummy.tex") {
                textures[textureIndex] = Task.FromResult((Texture2DShaderResource?) null);
                return false;
            }

            Task<DdsFile?>? loader = null;
            this.DdsFileRequested?.Invoke(textureDefinition.TexturePath, ref loader);
            if (loader is null)
                return false;

            var pDevice = this._pDevice;
            pDevice->AddRef();
            textures[textureIndex] = task = loader.ContinueWith(
                r => {
                    try {
                        if (!r.IsCompletedSuccessfully || r.Result is null)
                            return null;
                        return new Texture2DShaderResource(pDevice, r.Result);
                    } finally {
                        pDevice->Release();
                    }
                });

            // Separate this out, since we want the task itself to be in completed state
            // when this callback is called.
            task.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
        }

        if (task is { IsCompletedSuccessfully: true, Result: { } result }) {
            pTexture = result.ShaderResourceView;
            return true;
        }

        pTexture = null;
        return false;
    }

    public void Draw(GameShaderState state)
    {
        this._pool.SetSamplers();
        this._pDeviceContext->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        this._pDeviceContext->IASetIndexBuffer(
            this._pIndexBuffers[this._lodIndex],
            DXGI_FORMAT.DXGI_FORMAT_R16_UINT,
            0);

        var lodInfo = this._mdl.Lods[this._lodIndex];
        foreach (var part in this.Enumerate(lodInfo.MeshIndex, lodInfo.MeshIndex + lodInfo.MeshCount)) {
            if (!this.TryGetMaterialAndShader(
                    part.Index,
                    out var materialIndex,
                    out var material,
                    out var shaderSet,
                    out var pInputLayout))
                continue;

            this._pDeviceContext->VSSetShader(shaderSet.Vs.Shader, null, 0);
            state.BindConstantBuffersFor(shaderSet.Vs.ShaderEntry);

            this._pDeviceContext->IASetInputLayout(pInputLayout);
            var vbuf = this._pVertexBuffers[this._lodIndex];
            this._pDeviceContext->IASetVertexBuffers(
                0,
                1,
                &vbuf,
                &part.Stride,
                &part.VertexOffset);

            this._pDeviceContext->PSSetShader(shaderSet.Ps.Shader, null, 0);
            state.BindConstantBuffersFor(shaderSet.Ps.ShaderEntry);

            this._pool.SetShaderResourcesToDummyTexture(0, 4);

            for (var j = 0; j < material.Textures.Length; j++) {
                var t = material.Textures[j];
                if (!this.TryGetTexture(materialIndex, j, out var pTexture))
                    continue;

                switch (t.TextureUsageSimple) {
                    case Texture.Usage.Diffuse:
                        this._pDeviceContext->PSSetShaderResources(0, 1, &pTexture);
                        break;
                    case Texture.Usage.Normal:
                        this._pDeviceContext->PSSetShaderResources(1, 1, &pTexture);
                        break;
                    case Texture.Usage.Specular:
                        this._pDeviceContext->PSSetShaderResources(2, 1, &pTexture);
                        break;
                    case Texture.Usage.Mask:
                        this._pDeviceContext->PSSetShaderResources(3, 1, &pTexture);
                        break;
                }
            }

            this._pDeviceContext->DrawIndexed(part.IndexCount, part.IndexOffset, 0);
        }
    }

    public readonly struct MeshPart {
        public readonly int Index;
        public readonly uint VertexOffset;
        public readonly uint Stride;
        public readonly uint IndexOffset;
        public readonly uint IndexCount;

        public MeshPart(int index, uint vertexOffset, uint stride, uint indexOffset, uint indexCount)
        {
            this.Index = index;
            this.VertexOffset = vertexOffset;
            this.Stride = stride;
            this.IndexOffset = indexOffset;
            this.IndexCount = indexCount;
        }
    }
}
