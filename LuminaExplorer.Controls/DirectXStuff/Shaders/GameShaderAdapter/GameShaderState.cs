using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using ShaderType = LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles.ShaderType;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// Per-renderer state for rendering with game shaders: scene-wide constant buffers, identified by the CRC32 of their
/// names, and intermediate render targets (G-buffers and light buffers).
/// </summary>
public sealed unsafe class GameShaderState : DirectXObject {
    /// <summary>Number of G-buffer render targets. Character G passes write to 5 render targets.</summary>
    public const int GBufferCount = 5;

    /// <summary>Number of light buffer render targets: diffuse and specular.</summary>
    public const int LightBufferCount = 2;

    private const DXGI_FORMAT RenderTargetFormat = DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT;

    private readonly GameShaderPool _pool;
    private readonly Dictionary<uint, ConstantBufferEntry> _constantBuffers = new();
    private readonly nint[] _pTextures = new nint[GBufferCount + LightBufferCount];
    private readonly nint[] _pRenderTargetViews = new nint[GBufferCount + LightBufferCount];
    private readonly nint[] _pShaderResourceViews = new nint[GBufferCount + LightBufferCount];
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;

    public GameShaderState(GameShaderPool pool)
    {
        this._pool = pool;
        pool.CopyDeviceAndContext(out this._pDevice, out this._pDeviceContext);
    }

    ~GameShaderState() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        foreach (var entry in this._constantBuffers.Values)
            SafeRelease(ref entry.Buffer);
        this._constantBuffers.Clear();
        this.ReleaseRenderTargets();
        SafeRelease(ref this._pDeviceContext);
        SafeRelease(ref this._pDevice);
    }

    private void ReleaseRenderTargets()
    {
        for (var i = 0; i < this._pTextures.Length; i++) {
            if (this._pShaderResourceViews[i] != 0)
                ((ID3D11ShaderResourceView*) this._pShaderResourceViews[i])->Release();
            if (this._pRenderTargetViews[i] != 0)
                ((ID3D11RenderTargetView*) this._pRenderTargetViews[i])->Release();
            if (this._pTextures[i] != 0)
                ((ID3D11Texture2D*) this._pTextures[i])->Release();
            this._pShaderResourceViews[i] = this._pRenderTargetViews[i] = this._pTextures[i] = 0;
        }

        this.Width = this.Height = 0;
    }

    protected override void Dispose(bool disposing)
    {
        this.ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Gets or sets the structured buffer of joint matrices to bind as g_JointMatrixArray (and
    /// g_JointMatrixArrayPrev), for skinning vertex shaders. Not owned by this object.</summary>
    public ID3D11ShaderResourceView* JointMatrixArray { get; set; }

    /// <summary>Gets the CPU side data of a constant buffer for writing, creating one if it does not exist.</summary>
    /// <param name="id">CRC32 of the name of the constant buffer.</param>
    /// <param name="size">Minimum size in bytes.</param>
    /// <returns>Span to the data.</returns>
    public Span<byte> GetDataForWriting(uint id, int size)
    {
        var entry = this.GetEntry(id, size);
        entry.Dirty = true;
        return entry.Data;
    }

    public void Write<T>(uint id, int offset, T value) where T : unmanaged =>
        MemoryMarshal.Write(this.GetDataForWriting(id, offset + sizeof(T))[offset..], in value);

    public void Write<T>(uint id, int offset, ReadOnlySpan<T> values) where T : unmanaged =>
        MemoryMarshal.AsBytes(values).CopyTo(
            this.GetDataForWriting(id, offset + values.Length * sizeof(T))[offset..]);

    /// <summary>Gets the GPU side constant buffer, uploading the data if it has been modified.</summary>
    public ID3D11Buffer* GetBuffer(uint id, int size)
    {
        var entry = this.GetEntry(id, size);
        if (entry.Buffer is null || entry.BufferSize < entry.Data.Length) {
            SafeRelease(ref entry.Buffer);
            var desc = new D3D11_BUFFER_DESC(
                (uint) entry.Data.Length,
                (uint) D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
            fixed (void* pData = entry.Data) {
                var data = new D3D11_SUBRESOURCE_DATA { pSysMem = pData };
                fixed (ID3D11Buffer** ppBuffer = &entry.Buffer)
                    this._pDevice->CreateBuffer(&desc, &data, ppBuffer).Ensure();
            }

            entry.BufferSize = entry.Data.Length;
            entry.Dirty = false;
        } else if (entry.Dirty) {
            fixed (void* pData = entry.Data)
                this._pDeviceContext->UpdateSubresource((ID3D11Resource*) entry.Buffer, 0, null, pData, 0, 0);
            entry.Dirty = false;
        }

        return entry.Buffer;
    }

    private ConstantBufferEntry GetEntry(uint id, int size)
    {
        size = Math.Max(16, (size + 15) / 16 * 16);
        if (!this._constantBuffers.TryGetValue(id, out var entry))
            this._constantBuffers.Add(id, entry = new());
        if (entry.Data.Length < size) {
            var newData = new byte[size];
            entry.Data.CopyTo(newData, 0);
            entry.Data = newData;
            entry.Dirty = true;
        }

        return entry;
    }

    /// <summary>Ensures that the intermediate render targets are of the given size.</summary>
    public void EnsureRenderTargets(int width, int height)
    {
        if (this.Width == width && this.Height == height && this._pTextures[0] != 0)
            return;

        this.ReleaseRenderTargets();
        var desc = new D3D11_TEXTURE2D_DESC(
            RenderTargetFormat,
            (uint) width,
            (uint) height,
            1,
            1,
            (uint) (D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE));
        for (var i = 0; i < this._pTextures.Length; i++) {
            ID3D11Texture2D* pTexture;
            ID3D11RenderTargetView* pRtv;
            ID3D11ShaderResourceView* pSrv;
            this._pDevice->CreateTexture2D(&desc, null, &pTexture).Ensure();
            this._pTextures[i] = (nint) pTexture;
            this._pDevice->CreateRenderTargetView((ID3D11Resource*) pTexture, null, &pRtv).Ensure();
            this._pRenderTargetViews[i] = (nint) pRtv;
            this._pDevice->CreateShaderResourceView((ID3D11Resource*) pTexture, null, &pSrv).Ensure();
            this._pShaderResourceViews[i] = (nint) pSrv;
        }

        this.Width = width;
        this.Height = height;
    }

    public ID3D11RenderTargetView* GetGBufferRenderTarget(int index) =>
        (ID3D11RenderTargetView*) this._pRenderTargetViews[index];

    public ID3D11RenderTargetView* GetLightBufferRenderTarget(int index) =>
        (ID3D11RenderTargetView*) this._pRenderTargetViews[GBufferCount + index];

    public ID3D11ShaderResourceView* GetGBufferTexture(int index) =>
        (ID3D11ShaderResourceView*) this._pShaderResourceViews[index];

    /// <summary>Gets an intermediate render target as a texture, from the name of the resource.</summary>
    public ID3D11ShaderResourceView* GetRenderTargetTexture(uint id)
    {
        int index;
        if (id == GameShaderIds.SamplerGBuffer)
            index = 0;
        else if (id == GameShaderIds.SamplerGBuffer1)
            index = 1;
        else if (id == GameShaderIds.SamplerGBuffer2)
            index = 2;
        else if (id == GameShaderIds.SamplerGBuffer3)
            index = 3;
        else if (id == GameShaderIds.SamplerLightDiffuse)
            index = GBufferCount;
        else if (id == GameShaderIds.SamplerLightSpecular)
            index = GBufferCount + 1;
        else
            return null;
        return (ID3D11ShaderResourceView*) this._pShaderResourceViews[index];
    }

    /// <summary>Unbinds all shader resources, so that the render targets can be bound as outputs.</summary>
    public void UnbindShaderResources()
    {
        var nulls = stackalloc ID3D11ShaderResourceView*[D3D11.D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT];
        for (var i = 0; i < D3D11.D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT; i++)
            nulls[i] = null;
        this._pDeviceContext->PSSetShaderResources(0, D3D11.D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT, nulls);
        this._pDeviceContext->VSSetShaderResources(0, D3D11.D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT, nulls);
    }

    /// <summary>Binds the resources used by a shader.</summary>
    /// <param name="shader">The shader.</param>
    /// <param name="material">The material, which supplies material parameters, textures, and sampler states.</param>
    public void Bind(GameShaderSm5 shader, GameShaderMaterial? material)
    {
        var ctx = this._pDeviceContext;
        foreach (var c in shader.Constants) {
            ID3D11Buffer* pBuffer = null;
            if (material is not null && c.Id == GameShaderIds.MaterialParameter)
                pBuffer = material.MaterialParameterBuffer;
            if (pBuffer is null)
                pBuffer = this.GetBuffer(c.Id, c.Size);

            switch (shader.ShaderType) {
                case ShaderType.Vertex:
                    ctx->VSSetConstantBuffers(c.Slot, 1, &pBuffer);
                    break;
                case ShaderType.Pixel:
                    ctx->PSSetConstantBuffers(c.Slot, 1, &pBuffer);
                    break;
            }
        }

        foreach (var s in shader.Samplers) {
            var pSampler = material is not null ? material.GetSampler(s.Id) : null;
            if (pSampler is null)
                pSampler = this._pool.GetGlobalSampler(s.Id);

            switch (shader.ShaderType) {
                case ShaderType.Vertex:
                    ctx->VSSetSamplers(s.Slot, 1, &pSampler);
                    break;
                case ShaderType.Pixel:
                    ctx->PSSetSamplers(s.Slot, 1, &pSampler);
                    break;
            }
        }

        foreach (var t in shader.Textures) {
            ID3D11ShaderResourceView* pView;
            if (t.Id == GameShaderIds.JointMatrixArray || t.Id == GameShaderIds.JointMatrixArrayPrev) {
                // The previous frame's joint matrices are only used for motion vectors; use the current ones.
                pView = this.JointMatrixArray;
                switch (shader.ShaderType) {
                    case ShaderType.Vertex:
                        ctx->VSSetShaderResources(t.Slot, 1, &pView);
                        break;
                    case ShaderType.Pixel:
                        ctx->PSSetShaderResources(t.Slot, 1, &pView);
                        break;
                }

                continue;
            }

            pView = material is not null ? material.GetTexture(t.Id) : null;
            if (pView is null)
                pView = this.GetRenderTargetTexture(t.Id);
            if (pView is null)
                pView = this._pool.GetGlobalTexture(t.Id);
            if (pView is null)
                pView = this._pool.GetDummyTexture(t.Id, t.Dimension);

            switch (shader.ShaderType) {
                case ShaderType.Vertex:
                    ctx->VSSetShaderResources(t.Slot, 1, &pView);
                    break;
                case ShaderType.Pixel:
                    ctx->PSSetShaderResources(t.Slot, 1, &pView);
                    break;
            }
        }
    }

    private sealed class ConstantBufferEntry {
        public byte[] Data = [];
        public ID3D11Buffer* Buffer;
        public int BufferSize;
        public bool Dirty;
    }
}
