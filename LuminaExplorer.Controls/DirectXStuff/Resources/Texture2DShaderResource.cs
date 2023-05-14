using System;
using System.Runtime.InteropServices;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using WicNet;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public sealed unsafe class Texture2DShaderResource : D3D11Resource {
    private ID3D11Texture2D* _pTexture2D;
    private ID3D11ShaderResourceView* _pShaderResourceView;

    public Texture2DShaderResource(
        ID3D11Device* pDevice,
        Format format,
        uint width,
        uint height,
        uint stride,
        nint scan0) {
        try {
            var desc = new Texture2DDesc(
                width: width,
                height: height,
                mipLevels: 1,
                arraySize: 1,
                format: format,
                sampleDesc: new(1, 0),
                usage: Usage.Default,
                bindFlags: (uint) BindFlag.ShaderResource,
                cPUAccessFlags: 0,
                miscFlags: 0);
            var sr = new SubresourceData((void*) scan0, stride, stride * height);
            fixed (ID3D11Texture2D** ppResource = &_pTexture2D)
                ThrowH(pDevice->CreateTexture2D(&desc, &sr, ppResource));
            SetResource(_pTexture2D);
            _pShaderResourceView = ResourceUtils.CreateShaderResourceView(_pTexture2D, pDevice);
        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    public static Texture2DShaderResource FromWicBitmap(ID3D11Device* pDevice, WicBitmapSource bitmap) {
        WicBitmapSource? bitmapSource = null;
        IComObject<IWICBitmap>? wicBitmap = null;
        try {
            var format = (Format) PixFmtResolver.GetDxgiFormat(PixFmtResolver.GetPixelFormat(bitmap.PixelFormat));
            if (format == 0) {
                // format = Format.FormatR32G32B32A32Float;
                format = Format.FormatR8G8B8A8Unorm;
                var pConverter = WICImagingFactory.WithFactory(x => {
                    x.CreateFormatConverter(out var pConverterInner).ThrowOnError();
                    return pConverterInner;
                });
                try {
                    pConverter.Initialize(
                            bitmap.ComObject.Object,
                            // WicPixelFormat.GUID_WICPixelFormat128bppRGBAFloat,
                            WicPixelFormat.GUID_WICPixelFormat32bppRGBA,
                            WICBitmapDitherType.WICBitmapDitherTypeNone,
                            null,
                            0f,
                            WICBitmapPaletteType.WICBitmapPaletteTypeCustom)
                        .ThrowOnError();
                    bitmapSource = new(WICImagingFactory.CreateBitmapFromSource(new ComObject<IWICBitmapSource>(pConverter)));
                } finally {
                    Marshal.ReleaseComObject(pConverter);
                }
            } else {
                bitmapSource = bitmap.Clone();
            }
            
            wicBitmap = bitmapSource.AsBitmap();
            
            using var lb = wicBitmap.Lock(WICBitmapLockFlags.WICBitmapLockRead);
            lb.Object.GetSize(out var width, out var height).ThrowOnError();
            lb.Object.GetStride(out var stride).ThrowOnError();
            lb.Object.GetDataPointer(out _, out var pointer).ThrowOnError();

            return new(pDevice, format, (uint) width, (uint) height, (uint) stride, pointer);
        } finally {
            wicBitmap?.Dispose();
            bitmapSource?.Dispose();
        }
    }

    public Texture2DShaderResource(ID3D11Device* pDevice, TexFile tex) {
        if (!tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D) &&
            !tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            throw new ArgumentOutOfRangeException(nameof(tex), tex, @"Must be a 2D texture or a cube map.");

        try {
            _pTexture2D = tex.CreateD3DTextureResource<ID3D11Texture2D>(pDevice);
            SetResource(_pTexture2D);
            _pShaderResourceView = ResourceUtils.CreateShaderResourceView(_pTexture2D, pDevice);
        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    public Texture2DShaderResource(ID3D11Device* pDevice, DdsFile dds) {
        if (dds is {Is2D: false, IsCubeMap: false})
            throw new ArgumentOutOfRangeException(nameof(dds), dds, @"Must be a 2D texture or a cube map.");

        try {
            _pTexture2D = dds.CreateD3DTextureResource<ID3D11Texture2D>(pDevice);
            SetResource(_pTexture2D);
            _pShaderResourceView = ResourceUtils.CreateShaderResourceView(_pTexture2D, pDevice);
        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    public ID3D11ShaderResourceView* ShaderResourceView => _pShaderResourceView;

    protected override void Dispose(bool disposing) {
        SafeRelease(ref _pTexture2D);
        SafeRelease(ref _pShaderResourceView);
    }
}
