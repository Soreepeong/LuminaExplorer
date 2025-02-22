using System;
using Lumina.Data.Files;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public sealed unsafe class Texture2DShaderResource : D3D11Resource {
    private ID3D11Texture2D* _pTexture2D;
    private ID3D11ShaderResourceView* _pShaderResourceView;

    public Texture2DShaderResource(
        ID3D11Device* pDevice,
        DXGI_FORMAT format,
        uint width,
        uint height,
        uint stride,
        nint scan0)
    {
        try {
            var desc = new D3D11_TEXTURE2D_DESC(
                format,
                width,
                height,
                mipLevels: 1,
                bindFlags: (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE);
            var sr = new D3D11_SUBRESOURCE_DATA
                { pSysMem = (void*) scan0, SysMemPitch = stride, SysMemSlicePitch = stride * height };
            fixed (ID3D11Texture2D** ppResource = &this._pTexture2D)
                pDevice->CreateTexture2D(&desc, &sr, ppResource).Ensure();
            this.SetResource(this._pTexture2D);
            this._pShaderResourceView = ResourceUtils.CreateShaderResourceView(this._pTexture2D, pDevice);
        } catch (Exception) {
            this.Dispose();
            throw;
        }
    }

    public Texture2DShaderResource(ID3D11Device* pDevice, TexFile tex)
    {
        if (!tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D) &&
            !tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            throw new ArgumentOutOfRangeException(nameof(tex), tex, @"Must be a 2D texture or a cube map.");

        try {
            this._pTexture2D = tex.CreateD3DTextureResource<ID3D11Texture2D>(pDevice);
            this.SetResource(this._pTexture2D);
            this._pShaderResourceView = ResourceUtils.CreateShaderResourceView(this._pTexture2D, pDevice);
        } catch (Exception) {
            this.Dispose();
            throw;
        }
    }

    public Texture2DShaderResource(ID3D11Device* pDevice, DdsFile dds)
    {
        if (dds is { Is2D: false, IsCubeMap: false })
            throw new ArgumentOutOfRangeException(nameof(dds), dds, @"Must be a 2D texture or a cube map.");

        try {
            this._pTexture2D = dds.CreateD3DTextureResource<ID3D11Texture2D>(pDevice);
            this.SetResource(this._pTexture2D);
            this._pShaderResourceView = ResourceUtils.CreateShaderResourceView(this._pTexture2D, pDevice);
        } catch (Exception) {
            this.Dispose();
            throw;
        }
    }

    public ID3D11ShaderResourceView* ShaderResourceView => this._pShaderResourceView;

    public static Texture2DShaderResource FromWicBitmap(ID3D11Device* pDevice, ComPtr<IWICBitmapSource> bitmapSource)
    {
        bitmapSource.GetMetrics(out _, out _, out var pixelFormat);
        var format = PixFmtResolver.GetDxgiFormat(PixFmtResolver.GetPixelFormat(pixelFormat));
        using var bitmap = format == 0
            ? bitmapSource.AsBitmap(GUID.GUID_WICPixelFormat32bppBGRA)
            : bitmapSource.AsBitmap();
        if (format == 0)
            format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;

        using var lb = bitmap.Lock(read: true);

        return new(pDevice, format, lb.Width, lb.Height, lb.Stride, (nint) lb.Data);
    }

    protected override void Dispose(bool disposing)
    {
        SafeRelease(ref this._pTexture2D);
        SafeRelease(ref this._pShaderResourceView);
    }
}
