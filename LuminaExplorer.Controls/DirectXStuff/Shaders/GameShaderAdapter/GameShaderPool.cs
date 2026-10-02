using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using ShaderType = LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles.ShaderType;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// Holds objects shared between models rendered using game shaders: shader packages, compiled shaders, sampler
/// states, placeholder textures, game-wide textures, and render states.
/// </summary>
public sealed unsafe class GameShaderPool : DirectXObject {
    /// <summary>Game-wide textures that are not specified by materials.</summary>
    private static readonly (string Name, string Path)[] GlobalTexturePaths = [
        ("g_SamplerTileOrb", "chara/common/texture/tile_orb_array.tex"),
        ("g_SamplerTileNormal", "chara/common/texture/tile_norm_array.tex"),
        ("g_SamplerSphereMap", "chara/common/texture/sphere_d_array.tex"),
    ];

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, Task<ShpkFile?>> _shpkFiles = new();
    private readonly Dictionary<(ShpkFile, ShaderType, uint), GameShaderSm5> _shaders = new();
    private readonly Dictionary<
        (D3D11_FILTER, D3D11_TEXTURE_ADDRESS_MODE, D3D11_TEXTURE_ADDRESS_MODE, float, float, float),
        nint> _samplers = new();
    private readonly Dictionary<(DummyKind, D3D_SRV_DIMENSION), (nint Texture, nint View)> _dummies = new();
    private readonly Dictionary<uint, Task<Texture2DShaderResource?>?> _globalTextures = new();

    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11RasterizerState* _pRasterizerCullBack;
    private ID3D11RasterizerState* _pRasterizerCullNone;
    private ID3D11DepthStencilState* _pDepthWrite;
    private ID3D11DepthStencilState* _pDepthTestOnly;
    private ID3D11DepthStencilState* _pDepthDisabled;
    private ID3D11BlendState* _pBlendOpaque;
    private ID3D11BlendState* _pBlendAlpha;
    private ID3D11VertexShader* _pLightingVs;
    private ID3D11PixelShader* _pLightingPs;

    public GameShaderPool(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
    {
        try {
            this._pDevice = pDevice;
            this._pDevice->AddRef();
            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();

            // Same as what DirectXRenderer uses (D3D11_CULL_FRONT with clockwise front faces), as models use
            // counter-clockwise front faces with the camera used in model viewer.
            var rasterizerDesc = new D3D11_RASTERIZER_DESC(
                fillMode: D3D11_FILL_MODE.D3D11_FILL_SOLID,
                cullMode: D3D11_CULL_MODE.D3D11_CULL_FRONT,
                frontCounterClockwise: false,
                depthBias: 0,
                slopeScaledDepthBias: 0,
                depthBiasClamp: 0,
                depthClipEnable: true,
                scissorEnable: false,
                multisampleEnable: true,
                antialiasedLineEnable: false);
            fixed (ID3D11RasterizerState** pp = &this._pRasterizerCullBack)
                pDevice->CreateRasterizerState(&rasterizerDesc, pp).Ensure();
            rasterizerDesc.CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE;
            fixed (ID3D11RasterizerState** pp = &this._pRasterizerCullNone)
                pDevice->CreateRasterizerState(&rasterizerDesc, pp).Ensure();

            var depthDesc = new D3D11_DEPTH_STENCIL_DESC {
                DepthEnable = true,
                DepthWriteMask = D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ALL,
                DepthFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_LESS_EQUAL,
                StencilEnable = false,
            };
            fixed (ID3D11DepthStencilState** pp = &this._pDepthWrite)
                pDevice->CreateDepthStencilState(&depthDesc, pp).Ensure();
            depthDesc.DepthWriteMask = D3D11_DEPTH_WRITE_MASK.D3D11_DEPTH_WRITE_MASK_ZERO;
            fixed (ID3D11DepthStencilState** pp = &this._pDepthTestOnly)
                pDevice->CreateDepthStencilState(&depthDesc, pp).Ensure();
            depthDesc.DepthEnable = false;
            fixed (ID3D11DepthStencilState** pp = &this._pDepthDisabled)
                pDevice->CreateDepthStencilState(&depthDesc, pp).Ensure();

            var blendDesc = new D3D11_BLEND_DESC();
            for (var i = 0; i < 8; i++) {
                blendDesc.RenderTarget[i] = new() {
                    BlendEnable = false,
                    SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE,
                    DestBlend = D3D11_BLEND.D3D11_BLEND_ZERO,
                    BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
                    SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
                    DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO,
                    BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
                    RenderTargetWriteMask = (byte) D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
                };
            }

            fixed (ID3D11BlendState** pp = &this._pBlendOpaque)
                pDevice->CreateBlendState(&blendDesc, pp).Ensure();

            blendDesc.RenderTarget[0].BlendEnable = true;
            blendDesc.RenderTarget[0].SrcBlend = D3D11_BLEND.D3D11_BLEND_SRC_ALPHA;
            blendDesc.RenderTarget[0].DestBlend = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA;
            blendDesc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE;
            blendDesc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_INV_SRC_ALPHA;
            fixed (ID3D11BlendState** pp = &this._pBlendAlpha)
                pDevice->CreateBlendState(&blendDesc, pp).Ensure();

            var bytecode = ResourceUtils.CompileShaderFromString(LightingShaderSource, "vs_5_0", "main_vs");
            fixed (ID3D11VertexShader** pp = &this._pLightingVs)
            fixed (void* pBytecode = bytecode)
                pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, pp).Ensure();

            bytecode = ResourceUtils.CompileShaderFromString(LightingShaderSource, "ps_5_0", "main_ps");
            fixed (ID3D11PixelShader** pp = &this._pLightingPs)
            fixed (void* pBytecode = bytecode)
                pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, pp).Ensure();
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~GameShaderPool() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        lock (this._syncRoot) {
            foreach (var p in this._samplers.Values)
                ((ID3D11SamplerState*) p)->Release();
            this._samplers.Clear();
            foreach (var (texture, view) in this._dummies.Values) {
                ((ID3D11ShaderResourceView*) view)->Release();
                ((ID3D11Texture2D*) texture)->Release();
            }

            this._dummies.Clear();
        }

        SafeRelease(ref this._pRasterizerCullBack);
        SafeRelease(ref this._pRasterizerCullNone);
        SafeRelease(ref this._pDepthWrite);
        SafeRelease(ref this._pDepthTestOnly);
        SafeRelease(ref this._pDepthDisabled);
        SafeRelease(ref this._pBlendOpaque);
        SafeRelease(ref this._pBlendAlpha);
        SafeRelease(ref this._pLightingVs);
        SafeRelease(ref this._pLightingPs);
        SafeRelease(ref this._pDevice);
        SafeRelease(ref this._pDeviceContext);
    }

    private void DisposePrivate(bool disposing)
    {
        if (disposing) {
            lock (this._syncRoot) {
                foreach (var s in this._shaders.Values)
                    s.Dispose();
                this._shaders.Clear();
                foreach (var t in this._globalTextures.Values)
                    t?.ContinueWith(r => r.Result?.Dispose());
                this._globalTextures.Clear();
            }
        }

        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public event ShaderEvents.FileRequested<ShpkFile>? ShpkFileRequested;

    public event ShaderEvents.FileRequested<TexFile>? TexFileRequested;

    /// <summary>Invoked when a resource requested by this pool finishes loading.</summary>
    public event Action? ResourceLoadStateChanged;

    public ID3D11Device* Device => this._pDevice;

    public ID3D11DeviceContext* DeviceContext => this._pDeviceContext;

    public ID3D11RasterizerState* RasterizerCullBack => this._pRasterizerCullBack;

    public ID3D11RasterizerState* RasterizerCullNone => this._pRasterizerCullNone;

    public ID3D11DepthStencilState* DepthWrite => this._pDepthWrite;

    public ID3D11DepthStencilState* DepthTestOnly => this._pDepthTestOnly;

    public ID3D11DepthStencilState* DepthDisabled => this._pDepthDisabled;

    public ID3D11BlendState* BlendOpaque => this._pBlendOpaque;

    public ID3D11BlendState* BlendAlpha => this._pBlendAlpha;

    public ID3D11VertexShader* LightingVertexShader => this._pLightingVs;

    public ID3D11PixelShader* LightingPixelShader => this._pLightingPs;

    public void CopyDeviceAndContext(out ID3D11Device* pDevice, out ID3D11DeviceContext* pDeviceContext)
    {
        pDevice = this._pDevice;
        pDevice->AddRef();
        pDeviceContext = this._pDeviceContext;
        pDeviceContext->AddRef();
    }

    public Task<ShpkFile?> GetShpk(string name)
    {
        lock (this._syncRoot) {
            if (this._shpkFiles.TryGetValue(name, out var task))
                return task;

            Task<ShpkFile?>? loader = null;
            this.ShpkFileRequested?.Invoke($"shader/sm5/shpk/{name}", ref loader);
            if (loader is null)
                return Task.FromResult((ShpkFile?) null);
            return this._shpkFiles[name] = loader;
        }
    }

    /// <summary>Gets the shaders of the specified pass of a node.</summary>
    /// <returns>The shader set, or null if the node does not have the pass or the shaders could not be created.
    /// </returns>
    public ShaderSet? GetShaderSet(ShpkFile shpk, in ShaderNode node, uint passId)
    {
        foreach (var pass in node.Passes) {
            if (pass.Id != passId)
                continue;
            if (pass.VertexShader >= shpk.VertexShaderEntries.Length ||
                pass.PixelShader >= shpk.PixelShaderEntries.Length)
                return null;

            // Tessellation and geometry shaders are not supported.
            if (pass.HullShader != uint.MaxValue || pass.DomainShader != uint.MaxValue ||
                pass.GeometryShader != uint.MaxValue)
                return null;

            var vs = (GameVertexShaderSm5) this.GetShader(shpk, ShaderType.Vertex, pass.VertexShader);
            var ps = (GamePixelShaderSm5) this.GetShader(shpk, ShaderType.Pixel, pass.PixelShader);
            return new(passId, vs, ps);
        }

        return null;
    }

    private GameShaderSm5 GetShader(ShpkFile shpk, ShaderType type, uint index)
    {
        lock (this._syncRoot) {
            if (this._shaders.TryGetValue((shpk, type, index), out var shader))
                return shader;

            shader = type switch {
                ShaderType.Vertex => new GameVertexShaderSm5(this._pDevice, shpk.VertexShaderEntries[index]),
                ShaderType.Pixel => new GamePixelShaderSm5(this._pDevice, shpk.PixelShaderEntries[index]),
                _ => throw new NotSupportedException(),
            };
            this._shaders.Add((shpk, type, index), shader);
            return shader;
        }
    }

    /// <summary>
    /// Selects a node from a shader package, using the shader keys from a material, and the default values for the
    /// other keys, rendering into the main view.
    /// </summary>
    /// <remarks>
    /// <para>The selector is the sum of key values each multiplied by a power of 31, computed for each of system,
    /// scene, material, and sub view keys, and then the four values are combined the same way.
    /// See <see cref="ShpkFile.BuildSelector(IEnumerable{uint},IEnumerable{uint},IEnumerable{uint},IEnumerable{uint})"/>.
    /// </para>
    /// <para>Sub view keys are set to the default value of the first one ("Default") and SUB_VIEW_MAIN, as the
    /// default value of the second one is usually SUB_VIEW_SHADOW_0, which is used to draw into the shadow maps.
    /// </para>
    /// <para>If the exact selector does not exist, the node that matches the most keys (with sub view keys
    /// weighing the most, followed by material keys) is used.</para>
    /// <para>If <paramref name="skinned"/> is set, the TransformView scene key is set to TransformViewSkin, which
    /// selects vertex shaders that transform vertices using g_JointMatrixArray instead of g_WorldViewMatrix; only nodes
    /// with that value are considered.</para>
    /// </remarks>
    /// <param name="shpk">The shader package.</param>
    /// <param name="mtrl">The material.</param>
    /// <param name="skinned">Whether to select a node with skinning vertex shaders.</param>
    /// <param name="node">The selected node.</param>
    /// <returns>Whether a node has been selected.</returns>
    public static bool TrySelectNode(ShpkFile shpk, MtrlFile mtrl, bool skinned, out ShaderNode node)
    {
        var systemKeys = shpk.SystemKeys.Select(x => x.DefaultValue).ToArray();
        var sceneKeys = shpk.SceneKeys.Select(x => x.DefaultValue).ToArray();
        var materialKeys = shpk.MaterialKeys.Select(x => x.DefaultValue).ToArray();
        var subViewKeys = shpk.SubViewKeys.Select(x => x.DefaultValue).ToArray();
        if (subViewKeys.Length >= 2)
            subViewKeys[1] = GameShaderIds.SubViewMain;

        foreach (var key in mtrl.ShaderKeys) {
            for (var i = 0; i < shpk.MaterialKeys.Length; i++) {
                if (shpk.MaterialKeys[i].Id == key.Category)
                    materialKeys[i] = key.Value;
            }
        }

        // The alpha clipping variant (which discards pixels with alpha below g_AlphaThreshold) is used for materials
        // with a nonzero alpha threshold.
        if (GetMaterialParameter(shpk, mtrl, GameShaderIds.AlphaThreshold) > 0) {
            for (var i = 0; i < shpk.SceneKeys.Length; i++) {
                if (shpk.SceneKeys[i].Id == GameShaderIds.ApplyAlphaClip)
                    sceneKeys[i] = GameShaderIds.ApplyAlphaClipOn;
            }
        }

        var transformViewIndex = Array.FindIndex(shpk.SceneKeys, x => x.Id == GameShaderIds.TransformView);
        if (skinned) {
            if (transformViewIndex == -1) {
                node = default;
                return false;
            }

            sceneKeys[transformViewIndex] = GameShaderIds.TransformViewSkin;
        }

        var selector = ShpkFile.BuildSelector(systemKeys, sceneKeys, materialKeys, subViewKeys);
        if (shpk.TryGetNodeBySelector(selector, out node))
            return true;

        var bestScore = -1;
        foreach (var candidate in shpk.Nodes) {
            if (skinned &&
                (transformViewIndex >= candidate.SceneKeys.Length ||
                    candidate.SceneKeys[transformViewIndex] != GameShaderIds.TransformViewSkin))
                continue;

            var score = 0;
            for (var i = 0; i < Math.Min(subViewKeys.Length, candidate.SubViewKeys.Length); i++)
                score += candidate.SubViewKeys[i] == subViewKeys[i] ? 10000 : 0;
            for (var i = 0; i < Math.Min(materialKeys.Length, candidate.MaterialKeys.Length); i++)
                score += candidate.MaterialKeys[i] == materialKeys[i] ? 100 : 0;
            for (var i = 0; i < Math.Min(sceneKeys.Length, candidate.SceneKeys.Length); i++)
                score += candidate.SceneKeys[i] == sceneKeys[i] ? 1 : 0;
            for (var i = 0; i < Math.Min(systemKeys.Length, candidate.SystemKeys.Length); i++)
                score += candidate.SystemKeys[i] == systemKeys[i] ? 1 : 0;
            if (score > bestScore) {
                bestScore = score;
                node = candidate;
            }
        }

        return bestScore >= 0;
    }

    /// <summary>Gets the first float of a material parameter, from the material or the shader package defaults.
    /// </summary>
    public static float GetMaterialParameter(ShpkFile shpk, MtrlFile mtrl, uint id)
    {
        foreach (var constant in mtrl.Constants) {
            if (constant.ConstantId == id && constant.ValueSize >= 4 && constant.ValueOffset / 4 < mtrl.ShaderValues.Length)
                return mtrl.ShaderValues[constant.ValueOffset / 4];
        }

        foreach (var param in shpk.MaterialParams) {
            if (param.Id != id)
                continue;
            var bytes = shpk.GetMaterialParamDefault(param);
            return bytes.Length >= 4 ? BitConverter.ToSingle(bytes) : 0;
        }

        return 0;
    }

    /// <summary>Gets a sampler state, for the given material sampler flags.</summary>
    /// <remarks>
    /// Bits 0-1 and 2-3 are the address modes for U and V; bits 10-19 are the signed LoD bias times 64; and bits
    /// 20-23 are the minimum LoD.
    /// </remarks>
    public ID3D11SamplerState* GetSamplerFromMaterialFlags(uint flags, bool point)
    {
        var addressU = (D3D11_TEXTURE_ADDRESS_MODE) ((flags & 3u) + 1);
        var addressV = (D3D11_TEXTURE_ADDRESS_MODE) (((flags >> 2) & 3u) + 1);
        var lodBias = unchecked((int) (flags << 12) >> 22) / 64f;
        var minLod = (float) ((flags >> 20) & 0xFu);
        return this.GetSampler(
            point ? D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT : D3D11_FILTER.D3D11_FILTER_ANISOTROPIC,
            addressU,
            addressV,
            lodBias,
            minLod);
    }

    /// <summary>Gets a sampler state for a resource that is not specified from materials.</summary>
    public ID3D11SamplerState* GetGlobalSampler(uint id)
    {
        if (id == GameShaderIds.SamplerGBuffer)
            return this.GetSampler(
                D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                0,
                0);
        if (id == GameShaderIds.SamplerIndex)
            return this.GetSampler(
                D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                0,
                0);
        // The smaller mipmaps of the tile texture arrays (8x8 and below) are partially or entirely zero-filled in the
        // game files; limit sampling to the larger ones.
        if (id == GameShaderIds.SamplerTileOrb || id == GameShaderIds.SamplerTileNormal)
            return this.GetSampler(
                D3D11_FILTER.D3D11_FILTER_ANISOTROPIC,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                0,
                0,
                2);
        if (id == GameShaderIds.SamplerNormal || id == GameShaderIds.SamplerMask ||
            id == GameShaderIds.SamplerDiffuse)
            return this.GetSampler(
                D3D11_FILTER.D3D11_FILTER_ANISOTROPIC,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                0,
                0);
        return this.GetSampler(
            D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
            D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
            0,
            0);
    }

    private ID3D11SamplerState* GetSampler(
        D3D11_FILTER filter,
        D3D11_TEXTURE_ADDRESS_MODE addressU,
        D3D11_TEXTURE_ADDRESS_MODE addressV,
        float lodBias,
        float minLod,
        float maxLod = float.MaxValue)
    {
        lock (this._syncRoot) {
            var key = (filter, addressU, addressV, lodBias, minLod, maxLod);
            if (this._samplers.TryGetValue(key, out var p))
                return (ID3D11SamplerState*) p;

            var desc = new D3D11_SAMPLER_DESC(
                filter: filter,
                addressU: addressU,
                addressV: addressV,
                addressW: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                mipLODBias: lodBias,
                maxAnisotropy: filter == D3D11_FILTER.D3D11_FILTER_ANISOTROPIC ? 8u : 1u,
                comparisonFunc: D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                borderColor: null,
                minLOD: minLod,
                maxLOD: maxLod);
            ID3D11SamplerState* pSampler;
            this._pDevice->CreateSamplerState(&desc, &pSampler).Ensure();
            this._samplers.Add(key, (nint) pSampler);
            return pSampler;
        }
    }

    /// <summary>Gets a game-wide texture, such as tile textures.</summary>
    /// <returns>The texture, or null if it is not a known one or is not loaded yet.</returns>
    public ID3D11ShaderResourceView* GetGlobalTexture(uint id)
    {
        Task<Texture2DShaderResource?>? task;
        lock (this._syncRoot) {
            if (!this._globalTextures.TryGetValue(id, out task)) {
                var path = GlobalTexturePaths.FirstOrDefault(x => GameShaderIds.Crc(x.Name) == id).Path;
                if (path is not null) {
                    Task<TexFile?>? loader = null;
                    this.TexFileRequested?.Invoke(path, ref loader);
                    if (loader is not null) {
                        var pDevice = this._pDevice;
                        pDevice->AddRef();
                        task = loader.ContinueWith(
                            r => {
                                try {
                                    return r is { IsCompletedSuccessfully: true, Result: { } tex }
                                        ? Texture2DShaderResource.FromTexFileWithArraySlices(pDevice, tex)
                                        : null;
                                } finally {
                                    pDevice->Release();
                                }
                            });
                        task.ContinueWith(_ => this.ResourceLoadStateChanged?.Invoke());
                    }
                }

                this._globalTextures[id] = task;
            }
        }

        return task is { IsCompletedSuccessfully: true, Result: { } result } ? result.ShaderResourceView : null;
    }

    /// <summary>Describes the state of game-wide textures, for diagnostics.</summary>
    public string DescribeGlobalTextures()
    {
        lock (this._syncRoot) {
            return string.Join(
                "\n",
                this._globalTextures.Select(
                    x => $"{GlobalTexturePaths.FirstOrDefault(y => GameShaderIds.Crc(y.Name) == x.Key).Path}: " +
                         $"{x.Value?.Status} {x.Value?.Exception?.InnerException?.Message} " +
                         $"{(x.Value is { IsCompletedSuccessfully: true, Result: { } r } ? DescribeView(r.ShaderResourceView) : "-")}"));
        }
    }

    private static string DescribeView(ID3D11ShaderResourceView* pView)
    {
        D3D11_SHADER_RESOURCE_VIEW_DESC desc;
        pView->GetDesc(&desc);
        return $"{desc.ViewDimension} {desc.Format} mips={desc.Anonymous.Texture2DArray.MipLevels} " +
               $"arr={desc.Anonymous.Texture2DArray.ArraySize}";
    }

    /// <summary>Gets a placeholder texture for a resource that is not available.</summary>
    /// <returns>The texture, or null if a placeholder of the given dimension cannot be made.</returns>
    public ID3D11ShaderResourceView* GetDummyTexture(uint id, D3D_SRV_DIMENSION dimension)
    {
        DummyKind kind;
        if (id == GameShaderIds.SamplerNormal || id == GameShaderIds.SamplerNormal2 ||
            id == GameShaderIds.SamplerTileNormal)
            kind = DummyKind.FlatNormal;
        else if (id == GameShaderIds.SamplerDecal)
            kind = DummyKind.Transparent;
        else if (id == GameShaderIds.SamplerIndex || id == GameShaderIds.SamplerReflectionArray ||
                 id == GameShaderIds.SamplerLightSpecular || id == GameShaderIds.SamplerWrinklesMask ||
                 id == GameShaderIds.FogWeightLutSampler || id == GameShaderIds.SkySampler)
            kind = DummyKind.Black;
        else
            kind = DummyKind.White;

        lock (this._syncRoot) {
            if (this._dummies.TryGetValue((kind, dimension), out var pair))
                return (ID3D11ShaderResourceView*) pair.View;

            uint arraySize;
            uint miscFlags = 0;
            switch (dimension) {
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D:
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2DARRAY:
                    arraySize = 1;
                    break;
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURECUBE:
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURECUBEARRAY:
                    arraySize = 6;
                    miscFlags = (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_TEXTURECUBE;
                    break;
                default:
                    return null;
            }

            var color = kind switch {
                DummyKind.White => 0xFFFFFFFFu,
                DummyKind.Black => 0xFF000000u,
                DummyKind.Transparent => 0x00000000u,
                DummyKind.FlatNormal => 0xFFFF8080u,
                _ => 0u,
            };

            var data = stackalloc uint[16];
            for (var i = 0; i < 16; i++)
                data[i] = color;
            var subresources = stackalloc D3D11_SUBRESOURCE_DATA[(int) arraySize];
            for (var i = 0; i < arraySize; i++)
                subresources[i] = new() { pSysMem = data, SysMemPitch = 16, SysMemSlicePitch = 64 };

            var desc = new D3D11_TEXTURE2D_DESC(
                DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                4,
                4,
                arraySize,
                1,
                (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                miscFlags: miscFlags);
            ID3D11Texture2D* pTexture;
            this._pDevice->CreateTexture2D(&desc, subresources, &pTexture).Ensure();

            var viewDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC {
                Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                ViewDimension = dimension,
            };
            switch (dimension) {
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D:
                    viewDesc.Anonymous.Texture2D.MipLevels = 1;
                    break;
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2DARRAY:
                    viewDesc.Anonymous.Texture2DArray.MipLevels = 1;
                    viewDesc.Anonymous.Texture2DArray.ArraySize = 1;
                    break;
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURECUBE:
                    viewDesc.Anonymous.TextureCube.MipLevels = 1;
                    break;
                case D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURECUBEARRAY:
                    viewDesc.Anonymous.TextureCubeArray.MipLevels = 1;
                    viewDesc.Anonymous.TextureCubeArray.NumCubes = 1;
                    break;
            }

            ID3D11ShaderResourceView* pView;
            try {
                this._pDevice->CreateShaderResourceView((ID3D11Resource*) pTexture, &viewDesc, &pView).Ensure();
            } catch (Exception) {
                pTexture->Release();
                throw;
            }

            this._dummies.Add((kind, dimension), ((nint) pTexture, (nint) pView));
            return pView;
        }
    }

    private enum DummyKind {
        White,
        Black,
        Transparent,
        FlatNormal,
    }

    /// <summary>
    /// Fills the light buffers that the composite passes read, from the G-buffers written by the G passes.
    /// This replaces the game's own lighting passes, which require data (shadow maps, light lists, etc.) that the
    /// model viewer does not have.
    /// </summary>
    private const string LightingShaderSource = """
        Texture2D<float4> g_GBufferNormal : register(t0);
        Texture2D<float4> g_GBufferMisc : register(t1);

        cbuffer LightParameters : register(b0) {
            row_major float4x4 g_View; // world to view, applied as mul(v, g_View)
            float4 g_KeyDirection; // view space, pointing toward the light
            float4 g_KeyColor;
            float4 g_FillDirection;
            float4 g_FillColor;
            float4 g_SpecularParams; // x: intensity, y: power
        };

        struct VsOut {
            float4 Position : SV_Position;
        };

        VsOut main_vs(uint id : SV_VertexID) {
            VsOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            return o;
        }

        struct PsOut {
            float4 Diffuse : SV_Target0;
            float4 Specular : SV_Target1;
        };

        PsOut main_ps(VsOut i) {
            int3 p = int3(i.Position.xy, 0);
            float4 g0 = g_GBufferNormal.Load(p);
            float3 worldNormal = g0.xyz * 2 - 1;
            float3 n = mul(float4(worldNormal, 0), g_View).xyz;
            n = dot(n, n) > 0 ? normalize(n) : float3(0, 0, 1);

            float3 keyL = normalize(g_KeyDirection.xyz);
            float3 fillL = normalize(g_FillDirection.xyz);
            float3 v = float3(0, 0, 1);
            float3 h = normalize(keyL + v);

            PsOut o;
            o.Diffuse = float4(
                g_KeyColor.rgb * saturate(dot(n, keyL)) + g_FillColor.rgb * saturate(dot(n, fillL)),
                1);
            float spec = pow(saturate(dot(n, h)), g_SpecularParams.y) * g_SpecularParams.x;
            o.Specular = float4(g_KeyColor.rgb * spec * saturate(dot(n, keyL) * 4), 1);
            return o;
        }
        """;
}
