using System;
using System.IO;
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

    private Texture2DShaderResource(ID3D11Device* pDevice, ID3D11Texture2D* pTexture2D)
    {
        try {
            this._pTexture2D = pTexture2D;
            this.SetResource(this._pTexture2D);
            this._pShaderResourceView = ResourceUtils.CreateShaderResourceView(this._pTexture2D, pDevice);
        } catch (Exception) {
            this.Dispose();
            throw;
        }
    }

    public ID3D11ShaderResourceView* ShaderResourceView => this._pShaderResourceView;

    /// <summary>
    /// Creates a texture from a 2D texture file, keeping all array slices (unlike the conversion to
    /// <see cref="DdsFile"/>, which keeps only the first one). Only formats that need no conversion are supported.
    /// </summary>
    /// <remarks>Surfaces of array textures are stored mipmap by mipmap, each with all of the slices.</remarks>
    public static Texture2DShaderResource FromTexFileWithArraySlices(ID3D11Device* pDevice, TexFile tex)
    {
        var (formatInt, conversion) = TexFile.GetDxgiFormatFromTextureFormat(tex.Header.Format);
        var format = (DXGI_FORMAT) formatInt;
        if (conversion != TexFile.DxgiFormatConversion.NoConversion || format == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
            throw new NotSupportedException($"Unsupported format: {tex.Header.Format}");

        var width = (uint) tex.Header.Width;
        var height = (uint) tex.Header.Height;
        var arraySize = Math.Max(1u, (uint) tex.Header.ArraySize);
        var mipCount = Math.Max(1u, (uint) tex.Header.MipCount);
        var isBlockCompressed = format.IsBlockCompressed();
        var bitsPerPixel = isBlockCompressed
            ? format is DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM or DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM ? 4u : 8u
            : format switch {
                DXGI_FORMAT.DXGI_FORMAT_R8_UNORM or DXGI_FORMAT.DXGI_FORMAT_A8_UNORM => 8u,
                DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT or DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM => 16u,
                DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT => 64u,
                DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT => 128u,
                _ => 32u,
            };

        var data = tex.Data;
        var offset = (int) tex.Header.OffsetToSurface[0];
        var subresources = new D3D11_SUBRESOURCE_DATA[arraySize * mipCount];
        fixed (byte* pData = data)
        fixed (D3D11_SUBRESOURCE_DATA* pSubresources = subresources) {
            for (var mip = 0u; mip < mipCount; mip++) {
                for (var slice = 0u; slice < arraySize; slice++) {
                    var w = Math.Max(1u, width >> (int) mip);
                    var h = Math.Max(1u, height >> (int) mip);
                    uint pitch, rows;
                    if (isBlockCompressed) {
                        pitch = Math.Max(1u, (w + 3) / 4) * bitsPerPixel * 2;
                        rows = Math.Max(1u, (h + 3) / 4);
                    } else {
                        pitch = (w * bitsPerPixel + 7) / 8;
                        rows = h;
                    }

                    if (offset + pitch * rows > data.Length)
                        throw new InvalidDataException("Texture data is too short.");

                    pSubresources[slice * mipCount + mip] = new() {
                        pSysMem = pData + offset,
                        SysMemPitch = pitch,
                        SysMemSlicePitch = pitch * rows,
                    };
                    offset += (int) (pitch * rows);
                }
            }

            var desc = new D3D11_TEXTURE2D_DESC(
                format,
                width,
                height,
                arraySize,
                mipCount,
                (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE);
            ID3D11Texture2D* pTexture;
            pDevice->CreateTexture2D(&desc, pSubresources, &pTexture).Ensure();
            return new(pDevice, pTexture);
        }
    }

    /// <summary>Whether the texture only has a red channel that should be shown as gray.</summary>
    public bool ReplicateRedChannel { get; init; }

    public static Texture2DShaderResource FromRawSlice(ID3D11Device* pDevice, RawTextureSlice slice)
    {
        fixed (byte* pData = slice.Data) {
            return new(
                pDevice,
                slice.Format,
                checked((uint) slice.Width),
                checked((uint) slice.Height),
                checked((uint) slice.RowPitch),
                (nint) pData) {
                ReplicateRedChannel = slice.ReplicateRedChannel,
            };
        }
    }

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
        base.Dispose(disposing);
    }
}
