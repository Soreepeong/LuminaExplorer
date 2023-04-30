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
using LuminaExplorer.Core.Util.DdsStructs;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

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

    public ModelObjectWithGameShader(GameShaderPool pool, MdlFile mdl, int variantId = 1,
        LodLevel lod = LodLevel.Highest) {
        Debug.Assert(mdl.Meshes.Length == mdl.VertexDeclarations.Length,
            "Mesh.ReadVertices seems to be expecting Meshes and VertexDeclarations to have same length.");

        try {
            // Ensure that we at least have non-null arrays in case of exceptions.
            _materials = Array.Empty<Task<Material?>?>();
            _shaderSets = Array.Empty<Task<ShaderSet?>?>();
            _pInputLayouts = new ID3D11InputLayout*[0];
            _textures = Array.Empty<Task<Texture2DShaderResource?>?[]>();
            _pSamplers = Array.Empty<ID3D11SamplerState*[]>();
            _pIndexBuffers = new ID3D11Buffer*[0];
            _pVertexBuffers = new ID3D11Buffer*[0];

            _pool = pool;
            _pool.CopyDeviceAndContext(out _pDevice, out _pDeviceContext);

            _mdl = mdl;
            _variantId = variantId;
            _lodIndex = (int) lod;
            _materials = new Task<Material?>?[mdl.FileHeader.MaterialCount];
            _shaderSets = new Task<ShaderSet?>?[mdl.FileHeader.MaterialCount];
            _textures = new Task<Texture2DShaderResource?>[_materials.Length][];
            _pSamplers = new ID3D11SamplerState*[_materials.Length][];

            _pInputLayouts = new ID3D11InputLayout*[mdl.Meshes.Length];

            _pIndexBuffers = new ID3D11Buffer*[_mdl.FileHeader.LodCount];
            _pVertexBuffers = new ID3D11Buffer*[_mdl.FileHeader.LodCount];
            for (var i = 0; i < _mdl.FileHeader.LodCount; i++) {
                fixed (void* pData = &_mdl.Data[_mdl.FileHeader.IndexOffset[i]])
                fixed (ID3D11Buffer** ppBuffer = &_pIndexBuffers[i]) {
                    var data = new SubresourceData(pData);
                    var desc = new BufferDesc(
                        _mdl.FileHeader.IndexBufferSize[i],
                        Usage.Default,
                        (uint) BindFlag.IndexBuffer);
                    ThrowH(_pDevice->CreateBuffer(&desc, &data, ppBuffer));
                }

                fixed (void* pData = &_mdl.Data[_mdl.FileHeader.VertexOffset[i]])
                fixed (ID3D11Buffer** ppBuffer = &_pVertexBuffers[i]) {
                    var data = new SubresourceData(pData);
                    var desc = new BufferDesc(
                        _mdl.FileHeader.VertexBufferSize[i],
                        Usage.Default,
                        (uint) BindFlag.VertexBuffer);
                    ThrowH(_pDevice->CreateBuffer(&desc, &data, ppBuffer));
                }
            }
        } catch (Exception) {
            DisposeInner(true);
            throw;
        }
    }

    ~ModelObjectWithGameShader() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        for (var i = 0; i < _pInputLayouts.Length; i++)
            SafeRelease(ref _pInputLayouts[i]);
        for (var i = 0; i < _pIndexBuffers.Length; i++)
            SafeRelease(ref _pIndexBuffers[i]);
        for (var i = 0; i < _pVertexBuffers.Length; i++)
            SafeRelease(ref _pVertexBuffers[i]);
        foreach (var t in _pSamplers) {
            if (t is not null) {
                for (var i = 0; i < t.Length; i++)
                    SafeRelease(ref t[i]);
            }
        }

        SafeRelease(ref _pDeviceContext);
        SafeRelease(ref _pDevice);
    }

    private void DisposeInner(bool disposing) {
        if (disposing) {
            foreach (var t in _textures) {
                if (t is not null)
                    foreach (var j in t)
                        j?.Dispose();
            }
        }

        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposeInner(disposing);
        base.Dispose(disposing);
    }

    public event ShaderEvents.FileRequested<DdsFile>? DdsFileRequested;

    public event ShaderEvents.FileRequested<MtrlFile>? MtrlFileRequested;

    public event Action? ResourceLoadStateChanged;

    public void GetBuffers(LodLevel lod, out ID3D11Buffer* pVertexBuffer, out ID3D11Buffer* pIndexBuffer) {
        pVertexBuffer = _pVertexBuffers[(int) lod];
        pIndexBuffer = _pIndexBuffers[(int) lod];
    }

    public IEnumerable<MeshPart> Enumerate(
        int startMeshIndex,
        int meshCount) {
        for (var i = startMeshIndex; i < startMeshIndex + meshCount; i++) {
            var mesh = _mdl.Meshes[i];

            if (mesh.SubMeshCount == 0) {
                yield return new(
                    i,
                    mesh.VertexBufferOffset[_lodIndex],
                    mesh.VertexBufferStride[_lodIndex],
                    mesh.StartIndex,
                    mesh.IndexCount);
            } else {
                foreach (var sm in _mdl.Submeshes.Skip(mesh.SubMeshIndex).Take(mesh.SubMeshCount))
                    yield return new(
                        i,
                        mesh.VertexBufferOffset[_lodIndex],
                        mesh.VertexBufferStride[_lodIndex],
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
        out ID3D11InputLayout* pInputLayout) {
        materialIndex = _mdl.Meshes[meshIndex].MaterialIndex;
        material = null!;
        shaderSet = null!;
        pInputLayout = null;

        var materialTask = _materials[materialIndex];
        if (materialTask is null) {
            if (MtrlFileRequested is null)
                return false;

            var mtrlPathSpan = _mdl.Strings.AsSpan((int) _mdl.MaterialNameOffsets[materialIndex]);
            mtrlPathSpan = mtrlPathSpan[..mtrlPathSpan.IndexOf((byte) 0)];

            var mtrlPath = Encoding.UTF8.GetString(mtrlPathSpan);
            if (mtrlPath.StartsWith('/')) {
                mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, _variantId);
                if (mtrlPath is null) {
                    _materials[materialIndex] = Task.FromResult((Material?) null);
                    return false;
                }
            }

            Task<MtrlFile?>? loader = null;
            MtrlFileRequested?.Invoke(mtrlPath, ref loader);
            if (loader is null)
                return false;

            var pDevice = _pDevice;
            pDevice->AddRef();
            _materials[materialIndex] = materialTask = loader.ContinueWith(r => {
                try {
                    if (!r.IsCompletedSuccessfully || r.Result is not { } mtrlFile)
                        return null;
                    var m = new Material(mtrlFile);
                    var materialIndex = _mdl.Meshes[meshIndex].MaterialIndex;

                    _textures[materialIndex] = new Task<Texture2DShaderResource?>?[m.Textures.Length];
                    var samplers = _pSamplers[materialIndex] = new ID3D11SamplerState*[m.Textures.Length];

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
                    for (var i = 0; i < mtrlFile.Samplers.Length; i++) {
                        fixed (ID3D11SamplerState** ppSampler = &samplers[i])
                            ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSampler));
                    }

                    return m;
                } finally {
                    pDevice->Release();
                }
            });
            materialTask.ContinueWith(_ => ResourceLoadStateChanged?.Invoke());
        }

        if (materialTask is not {IsCompletedSuccessfully: true, Result: { } mat})
            return false;

        material = mat;

        if (_shaderSets[materialIndex] == null) {
            var t = _shaderSets[materialIndex] = _pool.GetShaderSet(_mdl, mat);
            if (t is null)
                return false;
            t.ContinueWith(_ => ResourceLoadStateChanged?.Invoke());
        }

        if (_shaderSets[materialIndex] is not {IsCompletedSuccessfully: true, Result: { } set})
            return false;

        pInputLayout = _pInputLayouts[meshIndex];
        if (pInputLayout is null) {
            pInputLayout = _pInputLayouts[meshIndex] = shaderSet.Vs.GetInputLayout(_mdl.VertexDeclarations[meshIndex]);
            pInputLayout->AddRef();
        }

        shaderSet = set;
        return true;
    }

    public bool TryGetTexture(int materialIndex, int textureIndex, out ID3D11ShaderResourceView* pTexture) {
        pTexture = null;
        if (_materials[materialIndex] is not {IsCompletedSuccessfully: true, Result: { } mat})
            return false;

        if (_textures[materialIndex] is not { } textures)
            return false;

        var task = textures[textureIndex];
        if (task is null) {
            var textureDefinition = mat.Textures[textureIndex];
            if (textureDefinition.TexturePath == "dummy.tex") {
                textures[textureIndex] = Task.FromResult((Texture2DShaderResource?) null);
                return false;
            }

            Task<DdsFile?>? loader = null;
            DdsFileRequested?.Invoke(textureDefinition.TexturePath, ref loader);
            if (loader is null)
                return false;

            var pDevice = _pDevice;
            pDevice->AddRef();
            textures[textureIndex] = task = loader.ContinueWith(r => {
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
            task.ContinueWith(_ => ResourceLoadStateChanged?.Invoke());
        }

        if (task is {IsCompletedSuccessfully: true, Result: { } result}) {
            pTexture = result.ShaderResourceView;
            return true;
        }

        pTexture = null;
        return false;
    }

    public void Draw(GameShaderState state) {
        _pool.SetSamplers();
        _pDeviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);
        _pDeviceContext->IASetIndexBuffer(_pIndexBuffers[_lodIndex], Format.FormatR16Uint, 0);

        var lodInfo = _mdl.Lods[_lodIndex];
        foreach (var part in Enumerate(lodInfo.MeshIndex, lodInfo.MeshIndex + lodInfo.MeshCount)) {
            if (!TryGetMaterialAndShader(
                    part.Index,
                    out var materialIndex,
                    out var material,
                    out var shaderSet,
                    out var pInputLayout))
                continue;

            _pDeviceContext->VSSetShader(shaderSet.Vs.Shader, null, 0);
            state.BindConstantBuffersFor(shaderSet.Vs.ShaderEntry);

            _pDeviceContext->IASetInputLayout(pInputLayout);
            _pDeviceContext->IASetVertexBuffers(0, 1, _pVertexBuffers[_lodIndex], part.Stride, part.VertexOffset);

            _pDeviceContext->PSSetShader(shaderSet.Ps.Shader, null, 0);
            state.BindConstantBuffersFor(shaderSet.Ps.ShaderEntry);

            _pool.SetShaderResourcesToDummyTexture(0, 4);

            for (var j = 0; j < material.Textures.Length; j++) {
                var t = material.Textures[j];
                if (!TryGetTexture(materialIndex, j, out var pTexture))
                    continue;

                switch (t.TextureUsageSimple) {
                    case Texture.Usage.Diffuse:
                        _pDeviceContext->PSSetShaderResources(0, 1, pTexture);
                        break;
                    case Texture.Usage.Normal:
                        _pDeviceContext->PSSetShaderResources(1, 1, pTexture);
                        break;
                    case Texture.Usage.Specular:
                        _pDeviceContext->PSSetShaderResources(2, 1, pTexture);
                        break;
                    case Texture.Usage.Mask:
                        _pDeviceContext->PSSetShaderResources(3, 1, pTexture);
                        break;
                }
            }

            _pDeviceContext->DrawIndexed(part.IndexCount, part.IndexOffset, 0);
        }
    }

    public readonly struct MeshPart {
        public readonly int Index;
        public readonly uint VertexOffset;
        public readonly uint Stride;
        public readonly uint IndexOffset;
        public readonly uint IndexCount;

        public MeshPart(int index, uint vertexOffset, uint stride, uint indexOffset, uint indexCount) {
            Index = index;
            VertexOffset = vertexOffset;
            Stride = stride;
            IndexOffset = indexOffset;
            IndexCount = indexCount;
        }
    }
}
