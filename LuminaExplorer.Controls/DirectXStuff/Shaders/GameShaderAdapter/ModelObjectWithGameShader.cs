using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// A model to be rendered with game shaders. Vertex and index buffers are uploaded as-is from the model file, and
/// vertex streams are mapped to the vertex shader inputs by their semantics.
/// </summary>
/// <remarks>
/// Meshes with bone tables and blend weights and indices are drawn with skinning shaders (TransformViewSkin), which
/// read joint matrices from g_JointMatrixArray: a structured buffer of float3x4 matrices (column major; 48 bytes
/// each) that transform vertices from the model space directly into the view space, indexed by the blend indices,
/// which refer to the bone table of the mesh. One such buffer is kept for each bone table; see
/// <see cref="UpdateJointMatrices"/>.
/// </remarks>
public sealed unsafe class ModelObjectWithGameShader : DirectXObject {
    /// <summary>Input slot used for the vertex shader inputs that the model does not provide.</summary>
    private const uint DummyInputSlot = 2;

    /// <summary>Size of a joint matrix in g_JointMatrixArray.</summary>
    private const int JointMatrixStride = 48;

    private readonly GameShaderPool _pool;
    private readonly MdlFile _mdl;
    private readonly int _variantId;
    private readonly int _lodIndex;
    private readonly Task<GameShaderMaterial?>?[] _materials;
    private readonly Dictionary<(int, GameVertexShaderSm5), nint> _pInputLayouts = new();
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11Buffer* _pIndexBuffer;
    private ID3D11Buffer* _pVertexBuffer;
    private ID3D11Buffer* _pDummyVertexBuffer;
    private readonly bool[] _meshSkinned;
    private readonly nint[] _pJointBuffers;
    private readonly nint[] _pJointViews;

    public ModelObjectWithGameShader(GameShaderPool pool, MdlFile mdl, int variantId = 1, int lodIndex = 0)
    {
        this._materials = [];
        this._meshSkinned = [];
        this._pJointBuffers = this._pJointViews = [];
        try {
            this._pool = pool;
            this._pool.CopyDeviceAndContext(out this._pDevice, out this._pDeviceContext);

            this._mdl = mdl;
            this._variantId = variantId;
            this._lodIndex = Math.Clamp(lodIndex, 0, Math.Max(0, mdl.FileHeader.LodCount - 1));
            this._materials = new Task<GameShaderMaterial?>?[mdl.FileHeader.MaterialCount];

            fixed (void* pData = &this._mdl.Data[this._mdl.FileHeader.IndexOffset[this._lodIndex]])
            fixed (ID3D11Buffer** ppBuffer = &this._pIndexBuffer) {
                var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                var desc = new D3D11_BUFFER_DESC(
                    this._mdl.FileHeader.IndexBufferSize[this._lodIndex],
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);
                this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
            }

            fixed (void* pData = &this._mdl.Data[this._mdl.FileHeader.VertexOffset[this._lodIndex]])
            fixed (ID3D11Buffer** ppBuffer = &this._pVertexBuffer) {
                var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                var desc = new D3D11_BUFFER_DESC(
                    this._mdl.FileHeader.VertexBufferSize[this._lodIndex],
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
                this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
            }

            var ones = stackalloc float[] { 1f, 1f, 1f, 1f };
            fixed (ID3D11Buffer** ppBuffer = &this._pDummyVertexBuffer) {
                var data = new D3D11_SUBRESOURCE_DATA { pSysMem = ones };
                var desc = new D3D11_BUFFER_DESC(16, (uint) D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
                this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
            }

            this._meshSkinned = new bool[mdl.Meshes.Length];
            for (var i = 0; i < this._meshSkinned.Length; i++) {
                if (mdl.Meshes[i].BoneTableIndex >= mdl.BoneTables.Length || i >= mdl.VertexDeclarations.Length)
                    continue;
                var usages = mdl.VertexDeclarations[i].VertexElements
                    .TakeWhile(x => x.Stream != 255)
                    .Select(x => (Vertex.VertexUsage) x.Usage)
                    .ToArray();
                this._meshSkinned[i] = usages.Contains(Vertex.VertexUsage.BlendWeights) &&
                    usages.Contains(Vertex.VertexUsage.BlendIndices);
            }

            this._pJointBuffers = new nint[mdl.BoneTables.Length];
            this._pJointViews = new nint[mdl.BoneTables.Length];
            for (var i = 0; i < mdl.BoneTables.Length; i++) {
                var count = Math.Max(1, JointCount(mdl.BoneTables[i]));
                var initialData = new float[count * JointMatrixStride / sizeof(float)];
                for (var j = 0; j < count; j++) {
                    initialData[j * 12 + 0] = 1;
                    initialData[j * 12 + 4] = 1;
                    initialData[j * 12 + 8] = 1;
                }

                var desc = new D3D11_BUFFER_DESC(
                    (uint) (count * JointMatrixStride),
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    D3D11_USAGE.D3D11_USAGE_DYNAMIC,
                    (uint) D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_WRITE,
                    (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_BUFFER_STRUCTURED,
                    JointMatrixStride);
                ID3D11Buffer* pBuffer;
                fixed (void* pInitialData = initialData) {
                    var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pInitialData };
                    this._pDevice->CreateBuffer(&desc, &data, &pBuffer).Ensure();
                }

                this._pJointBuffers[i] = (nint) pBuffer;

                var viewDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC {
                    Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                    ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_BUFFER,
                };
                viewDesc.Buffer.FirstElement = 0;
                viewDesc.Buffer.NumElements = (uint) count;
                ID3D11ShaderResourceView* pView;
                this._pDevice->CreateShaderResourceView((ID3D11Resource*) pBuffer, &viewDesc, &pView).Ensure();
                this._pJointViews[i] = (nint) pView;
            }
        } catch (Exception) {
            this.DisposeInner(true);
            throw;
        }
    }

    ~ModelObjectWithGameShader() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        foreach (var p in this._pInputLayouts.Values)
            ((ID3D11InputLayout*) p)->Release();
        this._pInputLayouts.Clear();
        for (var i = 0; i < this._pJointViews.Length; i++) {
            if (this._pJointViews[i] != 0)
                ((ID3D11ShaderResourceView*) this._pJointViews[i])->Release();
            if (this._pJointBuffers[i] != 0)
                ((ID3D11Buffer*) this._pJointBuffers[i])->Release();
            this._pJointViews[i] = this._pJointBuffers[i] = 0;
        }

        SafeRelease(ref this._pIndexBuffer);
        SafeRelease(ref this._pVertexBuffer);
        SafeRelease(ref this._pDummyVertexBuffer);
        SafeRelease(ref this._pDeviceContext);
        SafeRelease(ref this._pDevice);
    }

    private void DisposeInner(bool disposing)
    {
        if (disposing) {
            foreach (var t in this._materials)
                t?.ContinueWith(r => r.Result?.Dispose());
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

    public MdlFile Mdl => this._mdl;

    public enum DrawStage {
        /// <summary>Draws opaque materials into the G-buffers.</summary>
        GBuffer,

        /// <summary>Draws opaque materials into the final render target, using the light buffers.</summary>
        Composite,

        /// <summary>Draws semi-transparent materials into the G-buffers.</summary>
        GBufferSemiTransparent,

        /// <summary>Draws semi-transparent materials into the final render target, using the light buffers.</summary>
        CompositeSemiTransparent,

        /// <summary>Draws materials without deferred passes.</summary>
        Forward,
    }

    /// <summary>Whether any of the loaded materials has semi-transparent passes.</summary>
    public bool HasSemiTransparentMaterials => this.LoadedMaterials.Any(x => x.HasSemiTransparentPasses);

    /// <summary>Updates the joint matrices used by skinned meshes.</summary>
    /// <param name="modelView">Transformation from the model space into the view space.</param>
    /// <param name="animator">Animator that supplies the skinning matrices, or null to use the bind pose.</param>
    public void UpdateJointMatrices(Matrix4x4 modelView, AnimatingJointsConstantBufferResource? animator)
    {
        for (var tableIndex = 0; tableIndex < this._pJointBuffers.Length; tableIndex++) {
            var pBuffer = (ID3D11Resource*) this._pJointBuffers[tableIndex];
            if (pBuffer is null)
                continue;

            D3D11_MAPPED_SUBRESOURCE mapped;
            if (this._pDeviceContext->Map(pBuffer, 0, D3D11_MAP.D3D11_MAP_WRITE_DISCARD, 0, &mapped).FAILED)
                continue;

            var count = Math.Max(1, JointCount(this._mdl.BoneTables[tableIndex]));
            var p = (float*) mapped.pData;
            for (var i = 0; i < count; i++, p += JointMatrixStride / sizeof(float)) {
                var m = animator is null
                    ? modelView
                    : animator.GetBoneTableJointMatrix(tableIndex, i) * modelView;

                // Each column of float3x4 is stored contiguously; with row vectors (as in System.Numerics), the
                // columns are the first three components of each row.
                p[0] = m.M11;
                p[1] = m.M12;
                p[2] = m.M13;
                p[3] = m.M21;
                p[4] = m.M22;
                p[5] = m.M23;
                p[6] = m.M31;
                p[7] = m.M32;
                p[8] = m.M33;
                p[9] = m.M41;
                p[10] = m.M42;
                p[11] = m.M43;
            }

            this._pDeviceContext->Unmap(pBuffer, 0);
        }
    }

    private static int JointCount(in MdlStructs.BoneTableStruct boneTable) =>
        Math.Min(boneTable.BoneCount, boneTable.BoneIndex?.Length ?? 0);

    public void Draw(GameShaderState state, DrawStage stage)
    {
        var ctx = this._pDeviceContext;
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->IASetIndexBuffer(this._pIndexBuffer, DXGI_FORMAT.DXGI_FORMAT_R16_UINT, 0);

        var lod = this._mdl.Lods[this._lodIndex];
        var buffers = stackalloc ID3D11Buffer*[3];
        var strides = stackalloc uint[3];
        var offsets = stackalloc uint[3];
        strides[2] = offsets[2] = 0;
        for (var meshIndex = (int) lod.MeshIndex; meshIndex < lod.MeshIndex + lod.MeshCount; meshIndex++) {
            var mesh = this._mdl.Meshes[meshIndex];
            if (mesh.IndexCount == 0 || !this.TryGetMaterial(mesh.MaterialIndex, out var material))
                continue;

            var skinned = this._meshSkinned[meshIndex] && material.Skinned is not null;
            var passes = skinned ? material.Skinned! : material.Rigid;
            var shaderSet = stage switch {
                DrawStage.GBuffer => passes.GPass,
                DrawStage.Composite => passes.CompositePass,
                DrawStage.GBufferSemiTransparent => passes.SemiTransparentGPass,
                DrawStage.CompositeSemiTransparent => passes.SemiTransparentCompositePass,
                DrawStage.Forward => passes.ForwardPass,
                _ => null,
            };
            if (shaderSet is null)
                continue;

            var pInputLayout = this.GetInputLayout(meshIndex, shaderSet.Vs);
            if (pInputLayout is null)
                continue;

            ctx->IASetInputLayout(pInputLayout);
            buffers[0] = buffers[1] = this._pVertexBuffer;
            buffers[2] = this._pDummyVertexBuffer;
            strides[0] = mesh.VertexBufferStride[0];
            strides[1] = mesh.VertexBufferStride[1];
            offsets[0] = mesh.VertexBufferOffset[0];
            offsets[1] = mesh.VertexBufferOffset[1];
            ctx->IASetVertexBuffers(0, 3, buffers, strides, offsets);

            ctx->RSSetState(material.HideBackfaces ? this._pool.RasterizerCullBack : this._pool.RasterizerCullNone);
            ctx->VSSetShader(shaderSet.Vs.Shader, null, 0);
            ctx->PSSetShader(shaderSet.Ps.Shader, null, 0);
            state.JointMatrixArray = skinned
                ? (ID3D11ShaderResourceView*) this._pJointViews[mesh.BoneTableIndex]
                : null;
            state.Bind(shaderSet.Vs, material);
            state.Bind(shaderSet.Ps, material);

            ctx->DrawIndexed(mesh.IndexCount, mesh.StartIndex, 0);
        }

        state.JointMatrixArray = null;
    }

    public bool TryGetMaterial(int materialIndex, [MaybeNullWhen(false)] out GameShaderMaterial material)
    {
        material = null!;
        if (materialIndex < 0 || materialIndex >= this._materials.Length)
            return false;

        var task = this._materials[materialIndex];
        if (task is null) {
            var mtrlPath = this._mdl.Strings.AsSpan((int) this._mdl.MaterialNameOffsets[materialIndex]).ExtractCString();
            if (mtrlPath.StartsWith('/')) {
                mtrlPath = Material.ResolveRelativeMaterialPath(
                    mtrlPath,
                    this._variantId,
                    strictSuffixValidation: false);
                if (mtrlPath is null) {
                    this._materials[materialIndex] = Task.FromResult((GameShaderMaterial?) null);
                    return false;
                }
            }

            Task<MtrlFile?>? loader = null;
            this.MtrlFileRequested?.Invoke(mtrlPath, ref loader);
            if (loader is null)
                return false;

            this._materials[materialIndex] = task = loader
                .ContinueWith(
                    r => r is { IsCompletedSuccessfully: true, Result: { } mtrl }
                        ? GameShaderMaterial.CreateAsync(this._pool, mtrlPath, mtrl)
                        : Task.FromResult((GameShaderMaterial?) null))
                .Unwrap()
                .ContinueWith(
                    r => {
                        if (r.Result is { } m) {
                            m.DdsFileRequested += this.MaterialOnDdsFileRequested;
                            m.ResourceLoadStateChanged += this.MaterialOnResourceLoadStateChanged;
                        }

                        return r.Result;
                    });
            task.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
        }

        if (task is not { IsCompletedSuccessfully: true, Result: { } result })
            return false;

        material = result;
        return true;
    }

    /// <summary>Gets the materials that have been loaded so far.</summary>
    public IEnumerable<GameShaderMaterial> LoadedMaterials =>
        this._materials
            .Where(x => x is { IsCompletedSuccessfully: true, Result: not null })
            .Select(x => x!.Result!);

    private void MaterialOnDdsFileRequested(string path, ref Task<DdsFile?>? loader) =>
        this.DdsFileRequested?.Invoke(path, ref loader);

    private void MaterialOnResourceLoadStateChanged() => this.ResourceLoadStateChanged?.Invoke();

    private ID3D11InputLayout* GetInputLayout(int meshIndex, GameVertexShaderSm5 vs)
    {
        if (this._pInputLayouts.TryGetValue((meshIndex, vs), out var p))
            return (ID3D11InputLayout*) p;

        var decl = this._mdl.VertexDeclarations[meshIndex];
        var available = new List<(string Semantic, uint Index, DXGI_FORMAT Format, uint Slot, uint Offset)>();
        var usageCounts = new Dictionary<Vertex.VertexUsage, uint>();
        foreach (var ve in decl.VertexElements) {
            if (ve.Stream == 255)
                break;

            var usage = (Vertex.VertexUsage) ve.Usage;
            var semantic = usage switch {
                Vertex.VertexUsage.Position => "POSITION",
                Vertex.VertexUsage.BlendWeights => "BLENDWEIGHT",
                Vertex.VertexUsage.BlendIndices => "BLENDINDICES",
                Vertex.VertexUsage.Normal => "NORMAL",
                Vertex.VertexUsage.UV => "TEXCOORD",
                Vertex.VertexUsage.Tangent2 => "TANGENT",
                Vertex.VertexUsage.Tangent1 => "BINORMAL",
                Vertex.VertexUsage.Color => "COLOR",
                _ => null,
            };
            var format = GetFormat(usage, ve.Type);
            if (semantic is null || format == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
                continue;

            usageCounts.TryGetValue(usage, out var semanticIndex);
            usageCounts[usage] = semanticIndex + 1;
            available.Add((semantic, semanticIndex, format, ve.Stream, ve.Offset));
        }

        var elements = new List<(string Semantic, uint Index, DXGI_FORMAT Format, uint Slot, uint Offset)>();
        foreach (var input in vs.InputParameters) {
            var match = available.FindIndex(
                x => x.Semantic == input.SemanticName && x.Index == input.SemanticIndex);
            if (match == -1) {
                elements.Add(
                    (input.SemanticName, input.SemanticIndex, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
                        DummyInputSlot, 0));
                continue;
            }

            // Skinning vertex shaders take BLENDWEIGHT and BLENDINDICES as integers, and unpack them by themselves:
            // the low byte of each component for the first 4 weights, and the high byte for the next 4 weights.
            var element = available[match];
            if (input.ComponentType is D3D_REGISTER_COMPONENT_TYPE.D3D_REGISTER_COMPONENT_UINT32
                or D3D_REGISTER_COMPONENT_TYPE.D3D_REGISTER_COMPONENT_SINT32)
                element.Format = ToIntegerFormat(element.Format);
            elements.Add(element);
        }

        var names = elements.Select(x => Marshal.StringToHGlobalAnsi(x.Semantic)).ToArray();
        try {
            var descs = stackalloc D3D11_INPUT_ELEMENT_DESC[Math.Max(1, elements.Count)];
            for (var i = 0; i < elements.Count; i++) {
                descs[i] = new() {
                    SemanticName = (sbyte*) names[i],
                    SemanticIndex = elements[i].Index,
                    Format = elements[i].Format,
                    InputSlot = elements[i].Slot,
                    AlignedByteOffset = elements[i].Offset,
                    InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    InstanceDataStepRate = 0,
                };
            }

            ID3D11InputLayout* pInputLayout = null;
            fixed (byte* pBytecode = vs.ByteCode) {
                if (this._pDevice->CreateInputLayout(
                        descs,
                        (uint) elements.Count,
                        pBytecode,
                        (nuint) vs.ByteCode.Length,
                        &pInputLayout).FAILED)
                    pInputLayout = null;
            }

            this._pInputLayouts[(meshIndex, vs)] = (nint) pInputLayout;
            return pInputLayout;
        } finally {
            foreach (var n in names)
                Marshal.FreeHGlobal(n);
        }
    }

    private static DXGI_FORMAT ToIntegerFormat(DXGI_FORMAT format) =>
        format switch {
            DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT,
            DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM => DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT,
            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT,
            _ => format,
        };

    private static DXGI_FORMAT GetFormat(Vertex.VertexUsage usage, byte type) =>
        type switch {
            0 => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
            1 => DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT,
            2 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
            3 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,
            5 => usage == Vertex.VertexUsage.BlendIndices
                ? DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT
                : DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            8 => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            13 => DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT,
            14 => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
            16 => usage == Vertex.VertexUsage.BlendIndices
                ? DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT
                : DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM,
            17 => usage == Vertex.VertexUsage.BlendIndices
                ? DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT
                : DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM,
            _ => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
        };
}
