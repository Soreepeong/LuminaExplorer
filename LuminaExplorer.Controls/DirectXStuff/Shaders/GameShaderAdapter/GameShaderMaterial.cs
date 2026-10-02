using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// A material prepared for rendering with game shaders: the selected shader node and its passes, the material
/// parameter constant buffer, the color table texture, and the textures and sampler states of the material.
/// </summary>
public sealed unsafe class GameShaderMaterial : DirectXObject {
    private readonly GameShaderPool _pool;
    private readonly Dictionary<uint, (string Path, uint Flags)> _samplerDefinitions = new();
    private readonly Dictionary<uint, Task<Texture2DShaderResource?>?> _textures = new();
    private readonly Dictionary<uint, nint> _pSamplers = new();
    private Texture2DShaderResource? _table;
    private ID3D11Buffer* _pMaterialParameterBuffer;

    private GameShaderMaterial(
        GameShaderPool pool,
        string path,
        MtrlFile mtrl,
        ShpkFile shpk,
        ShaderNode node,
        ShaderNode? skinnedNode)
    {
        this._pool = pool;
        this.Path = path;
        this.Mtrl = mtrl;
        this.Shpk = shpk;

        var pmf = mtrl as PenumbraMtrlFile;
        this.ShaderPackageName = pmf?.ShaderPackageName ?? string.Empty;
        var flags = pmf?.ShaderFlags ?? 1u;
        this.HideBackfaces = (flags & 0x1u) != 0;
        this.IsTransparent = (flags & 0x10u) != 0;
        this.ShaderFlags = flags;

        try {
            this.Rigid = new(pool, shpk, node, this.IsTransparent);
            if (skinnedNode is { } sn)
                this.Skinned = new(pool, shpk, sn, this.IsTransparent);

            // Material parameters: shader package defaults, overwritten with the values from the material.
            var paramBytes = new byte[Math.Max(16, ((int) shpk.Header.MaterialParamSize + 15) / 16 * 16)];
            shpk.MaterialParamsDefaults.AsSpan(0, Math.Min(shpk.MaterialParamsDefaults.Length, paramBytes.Length))
                .CopyTo(paramBytes);
            var shaderValues = MemoryMarshal.AsBytes(mtrl.ShaderValues.AsSpan());
            foreach (var constant in mtrl.Constants) {
                foreach (var param in shpk.MaterialParams) {
                    if (param.Id != constant.ConstantId)
                        continue;
                    var length = Math.Min(Math.Min(constant.ValueSize, param.ByteSize), paramBytes.Length - param.ByteOffset);
                    length = Math.Min(length, shaderValues.Length - constant.ValueOffset);
                    if (length > 0)
                        shaderValues.Slice(constant.ValueOffset, length).CopyTo(paramBytes.AsSpan(param.ByteOffset));
                }
            }

            fixed (void* pData = paramBytes)
            fixed (ID3D11Buffer** ppBuffer = &this._pMaterialParameterBuffer) {
                var desc = new D3D11_BUFFER_DESC(
                    (uint) paramBytes.Length,
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
                var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                pool.Device->CreateBuffer(&desc, &data, ppBuffer).Ensure();
            }

            // Color table: rows of 8 (Dawntrail) or 4 (older) half-precision float4 values.
            if (pmf is not null) {
                var (width, height) = pmf.DataSet.Length switch {
                    >= 32 * 64 => (8u, 32u),
                    >= 16 * 32 => (4u, 16u),
                    _ => (0u, 0u),
                };
                if (width != 0) {
                    fixed (byte* pTable = pmf.DataSet)
                        this._table = new(
                            pool.Device,
                            DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                            width,
                            height,
                            width * 8,
                            (nint) pTable);
                }
            }

            foreach (var sampler in mtrl.Samplers) {
                var texturePath = pmf?.GetTexturePath(sampler.TextureIndex) ?? string.Empty;
                this._samplerDefinitions[sampler.SamplerId] = (texturePath, sampler.Flags);
            }
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~GameShaderMaterial() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() => SafeRelease(ref this._pMaterialParameterBuffer);

    private void DisposePrivate(bool disposing)
    {
        if (disposing) {
            SafeDispose.One(ref this._table);
            foreach (var t in this._textures.Values)
                t?.ContinueWith(r => r.Result?.Dispose());
            this._textures.Clear();
        }

        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public event ShaderEvents.FileRequested<DdsFile>? DdsFileRequested;

    public event Action? ResourceLoadStateChanged;

    public string Path { get; }

    public MtrlFile Mtrl { get; }

    public ShpkFile Shpk { get; }

    public string ShaderPackageName { get; }

    public ShaderNode Node => this.Rigid.Node;

    public bool HideBackfaces { get; }

    public bool IsTransparent { get; }

    public uint ShaderFlags { get; }

    /// <summary>Passes of the node selected for meshes without skinning (TransformViewRigid).</summary>
    public PassSet Rigid { get; } = null!;

    /// <summary>Passes of the node selected for skinned meshes (TransformViewSkin), if the shader package has one.
    /// </summary>
    public PassSet? Skinned { get; }

    /// <summary>Whether this material has semi-transparent passes.</summary>
    public bool HasSemiTransparentPasses =>
        this.Rigid.SemiTransparentCompositePass is not null || this.Skinned?.SemiTransparentCompositePass is not null;

    public ID3D11Buffer* MaterialParameterBuffer => this._pMaterialParameterBuffer;

    public static Task<GameShaderMaterial?> CreateAsync(GameShaderPool pool, string path, MtrlFile mtrl)
    {
        var shpkName = (mtrl as PenumbraMtrlFile)?.ShaderPackageName;
        if (string.IsNullOrEmpty(shpkName))
            return Task.FromResult((GameShaderMaterial?) null);

        return pool.GetShpk(shpkName).ContinueWith(
            r => {
                if (r is not { IsCompletedSuccessfully: true, Result: { } shpk })
                    return null;
                if (!GameShaderPool.TrySelectNode(shpk, mtrl, false, out var node))
                    return null;
                ShaderNode? skinnedNode = GameShaderPool.TrySelectNode(shpk, mtrl, true, out var sn) ? sn : null;
                return (GameShaderMaterial?) new GameShaderMaterial(pool, path, mtrl, shpk, node, skinnedNode);
            });
    }

    /// <summary>Gets the sampler state for the given sampler, if the material specifies it.</summary>
    public ID3D11SamplerState* GetSampler(uint id)
    {
        if (this._pSamplers.TryGetValue(id, out var p))
            return (ID3D11SamplerState*) p;

        ID3D11SamplerState* pSampler = null;
        if (this._samplerDefinitions.TryGetValue(id, out var def))
            pSampler = this._pool.GetSamplerFromMaterialFlags(def.Flags, id == GameShaderIds.SamplerIndex);
        this._pSamplers[id] = (nint) pSampler;
        return pSampler;
    }

    /// <summary>Gets the texture for the given sampler, if the material specifies it and it has been loaded.</summary>
    public ID3D11ShaderResourceView* GetTexture(uint id)
    {
        if (id == GameShaderIds.SamplerTable)
            return this._table is null ? null : this._table.ShaderResourceView;

        if (!this._textures.TryGetValue(id, out var task)) {
            if (this._samplerDefinitions.TryGetValue(id, out var def) &&
                !string.IsNullOrEmpty(def.Path) &&
                !def.Path.EndsWith("/dummy.tex") &&
                def.Path != "dummy.tex") {
                Task<DdsFile?>? loader = null;
                this.DdsFileRequested?.Invoke(def.Path, ref loader);
                if (loader is null)
                    return null;

                var pDevice = this._pool.Device;
                pDevice->AddRef();
                task = loader.ContinueWith(
                    r => {
                        try {
                            return r is { IsCompletedSuccessfully: true, Result: { } dds }
                                ? new Texture2DShaderResource(pDevice, dds)
                                : null;
                        } finally {
                            pDevice->Release();
                        }
                    });
                task.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
            }

            this._textures[id] = task;
        }

        return task is { IsCompletedSuccessfully: true, Result: { } result } ? result.ShaderResourceView : null;
    }

    public override string ToString() =>
        $"{this.Path} ({this.ShaderPackageName}; flags {this.ShaderFlags:X}; rigid {this.Rigid}; " +
        $"skinned {this.Skinned?.ToString() ?? "-"})";

    /// <summary>Shaders for each pass of a shader node.</summary>
    public sealed class PassSet {
        public PassSet(GameShaderPool pool, ShpkFile shpk, ShaderNode node, bool isTransparent)
        {
            this.Node = node;
            var gOpaque = pool.GetShaderSet(shpk, node, GameShaderIds.PassGOpaque);
            var compositeOpaque = pool.GetShaderSet(shpk, node, GameShaderIds.PassCompositeOpaque);
            var gSemi = pool.GetShaderSet(shpk, node, GameShaderIds.PassGSemiTransparency) ?? gOpaque;
            var compositeSemi = pool.GetShaderSet(shpk, node, GameShaderIds.PassCompositeSemiTransparency);
            var forward = pool.GetShaderSet(shpk, node, GameShaderIds.PassSemiTransparency)
                ?? pool.GetShaderSet(shpk, node, GameShaderIds.Pass0);

            // Transparent materials (such as hair) are drawn twice in the game: the opaque passes draw the parts above
            // the alpha threshold, and the semi-transparent passes draw the rest with alpha blending.
            if (gOpaque is not null && compositeOpaque is not null) {
                this.GPass = gOpaque;
                this.CompositePass = compositeOpaque;
            }

            if ((isTransparent || this.CompositePass is null) && gSemi is not null && compositeSemi is not null) {
                this.SemiTransparentGPass = gSemi;
                this.SemiTransparentCompositePass = compositeSemi;
            }

            // Such as decals (charactertattoo.shpk) and occlusion (characterocclusion.shpk), which only blend into the
            // G-buffers, are not drawn at all.
            if (this.CompositePass is null && this.SemiTransparentCompositePass is null)
                this.ForwardPass = forward;
        }

        public ShaderNode Node { get; }

        /// <summary>PASS_G_OPAQUE: writes into the G-buffers.</summary>
        public ShaderSet? GPass { get; }

        /// <summary>PASS_COMPOSITE_OPAQUE: reads the light buffers and writes the final color.</summary>
        public ShaderSet? CompositePass { get; }

        /// <summary>PASS_G_SEMITRANSPARENCY: writes into the G-buffers, for the semi-transparent parts.</summary>
        public ShaderSet? SemiTransparentGPass { get; }

        /// <summary>PASS_COMPOSITE_SEMITRANSPARENCY: alpha blended into the final color.</summary>
        public ShaderSet? SemiTransparentCompositePass { get; }

        /// <summary>A pass that is drawn alone with alpha blending (PASS_SEMITRANSPARENCY or PASS_0), for shader
        /// packages without deferred passes.</summary>
        public ShaderSet? ForwardPass { get; }

        public override string ToString() =>
            $"node {this.Node.Id:X08}; " +
            $"passes {(this.CompositePass is null ? "" : "O")}{(this.SemiTransparentCompositePass is null ? "" : "S")}" +
            $"{(this.ForwardPass is null ? "" : "F")} " +
            $"({string.Join(", ", this.Node.Passes.Select(x => $"{x.Id:X08}"))})";
    }
}
