using System;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data.Files;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.DirectXStuff;

public static unsafe class ResourceUtils {
    public static byte[] CompileShaderFromAssemblyResource(
        this Type typeSharingNamespace,
        string target,
        string entrypointName = "main",
        string? fileName = null)
    {
        byte[] buffer;
        fileName ??= $"{typeSharingNamespace.Name}.hlsl";
        using (var stream = typeSharingNamespace.Assembly
                   .GetManifestResourceStream($"{typeSharingNamespace.Namespace}.{fileName}")!)
            stream.ReadExactly(buffer = new byte[stream.Length]);

        using var pCode = default(ComPtr<ID3DBlob>);
        using var pErrorMsgs = default(ComPtr<ID3DBlob>);
        fixed (void* pTarget = Encoding.UTF8.GetBytes(target))
        fixed (void* pEntrypointName = Encoding.UTF8.GetBytes(entrypointName))
        fixed (byte* pBuffer = &buffer[0]) {
            var hr = DirectX.D3DCompile(
                pBuffer,
                (nuint) buffer.Length,
                null,
                null,
                null,
                (sbyte*) pEntrypointName,
                (sbyte*) pTarget,
                D3DCOMPILE.D3DCOMPILE_DEBUG | D3DCOMPILE.D3DCOMPILE_SKIP_OPTIMIZATION,
                0,
                pCode.GetAddressOf(),
                pErrorMsgs.GetAddressOf());

            if (hr.FAILED) {
                if (!pErrorMsgs.IsEmpty()) {
                    throw new(
                        Encoding.UTF8.GetString(
                            (byte*) pErrorMsgs.Get()->GetBufferPointer(),
                            (int) pErrorMsgs.Get()->GetBufferSize()));
                }

                Marshal.ThrowExceptionForHR(hr);
            }
        }

        return new Span<byte>(pCode.Get()->GetBufferPointer(), (int) pCode.Get()->GetBufferSize()).ToArray();
    }

    public static T* CreateD3DTextureResource<T>(this DdsFile dds, ID3D11Device* pDevice)
        where T : unmanaged
    {
        T* pResource = null;
        var numImages = (uint) dds.NumImages;
        var numFaces = dds.IsCubeMap ? 6u : 1u;
        var numMipmaps = (uint) dds.NumMipmaps;

        var format = dds.PixFmt.DxgiFormat;
        if (format == DXGI_FORMAT.DXGI_FORMAT_UNKNOWN)
            throw new NotSupportedException("Not a supported DXGI format");

        var subresources = new D3D11_SUBRESOURCE_DATA[numImages, numFaces, numMipmaps];
        fixed (void* b = dds.Body)
        fixed (D3D11_SUBRESOURCE_DATA* pSubresources = subresources) {
            for (var i = 0; i < numImages; i++) {
                for (var j = 0; j < numFaces; j++) {
                    for (var k = 0; k < numMipmaps; k++) {
                        subresources[i, j, k] = new() {
                            pSysMem = (byte*) b + dds.MipmapDataOffset(i, j, k, out _) - dds.DataOffset,
                            SysMemPitch = (uint) dds.Pitch(k),
                            SysMemSlicePitch = (uint) dds.SliceSize(k),
                        };
                    }
                }
            }

            if (dds.Is1D) {
                var textureDesc = new D3D11_TEXTURE1D_DESC {
                    Width = (uint) dds.Width(0),
                    MipLevels = numMipmaps,
                    ArraySize = numImages,
                    Format = format,
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                pDevice->CreateTexture1D(&textureDesc, pSubresources, (ID3D11Texture1D**) &pResource).Ensure();
            } else if (dds.Is2D || dds.IsCubeMap) {
                var textureDesc = new D3D11_TEXTURE2D_DESC {
                    Width = (uint) dds.Width(0),
                    Height = (uint) dds.Height(0),
                    MipLevels = numMipmaps,
                    ArraySize = numImages * (dds.IsCubeMap ? 6u : 1u),
                    Format = format,
                    SampleDesc = new(1, 0),
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = dds.IsCubeMap ? (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_TEXTURECUBE : 0u,
                };
                pDevice->CreateTexture2D(&textureDesc, pSubresources, (ID3D11Texture2D**) &pResource).Ensure();
            } else if (dds.Is3D) {
                if (numImages != 1)
                    throw new NotSupportedException("3D Textures can only have 1 image.");
                var textureDesc = new D3D11_TEXTURE3D_DESC {
                    Width = (uint) dds.Width(0),
                    Height = (uint) dds.Height(0),
                    Depth = (uint) dds.Depth(0),
                    MipLevels = numMipmaps,
                    Format = format,
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                pDevice->CreateTexture3D(&textureDesc, pSubresources, (ID3D11Texture3D**) &pResource).Ensure();
            } else
                throw new NotSupportedException();
        }

        return pResource;
    }

    public static T* CreateD3DTextureResource<T>(this TexFile tex, ID3D11Device* pDevice)
        where T : unmanaged
    {
        T* pResource = null;
        var isCubeMap = tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube);
        var numFaces = isCubeMap ? 6u : 1u;
        var numMipmaps = tex.Header.MipCount;

        var (formatInt, conversion) = TexFile.GetDxgiFormatFromTextureFormat(tex.Header.Format);
        var format = (DXGI_FORMAT) formatInt;
        var buffer = tex.TextureBuffer;
        switch (conversion) {
            case TexFile.DxgiFormatConversion.NoConversion:
                break;
            case TexFile.DxgiFormatConversion.FromL8ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB4G4R4A4ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB5G5R5A1ToB8G8R8A8:
                buffer = buffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
                format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;
                break;
            default:
                throw new NotSupportedException();
        }

        var subresources = new D3D11_SUBRESOURCE_DATA[numFaces, numMipmaps];
        fixed (void* b = buffer.RawData)
        fixed (D3D11_SUBRESOURCE_DATA* pSubresources = subresources) {
            var bufferOffset = 0u;
            for (var j = 0; j < numFaces; j++) {
                for (var k = 0; k < numMipmaps; k++) {
                    var mipHeight = (uint) buffer.HeightOfMipmap(k);
                    var slicePitch = (uint) buffer.NumBytesOfMipmapPerPlane(k);
                    var depth = (uint) buffer.DepthOfMipmap(k);
                    var pitch = slicePitch / mipHeight;
                    if (pitch * mipHeight != slicePitch)
                        throw new NotSupportedException("pitch * height != slicePitch?");

                    subresources[j, k] = new() {
                        pSysMem = (byte*) b + bufferOffset,
                        SysMemPitch = pitch,
                        SysMemSlicePitch = slicePitch,
                    };
                    bufferOffset += slicePitch * depth;
                }
            }

            if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType1D)) {
                var textureDesc = new D3D11_TEXTURE1D_DESC {
                    Width = tex.Header.Width,
                    MipLevels = (uint) numMipmaps,
                    ArraySize = 1,
                    Format = format,
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                pDevice->CreateTexture1D(&textureDesc, pSubresources, (ID3D11Texture1D**) &pResource).Ensure();
            } else if (
                tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D) ||
                isCubeMap) {
                var textureDesc = new D3D11_TEXTURE2D_DESC {
                    Width = tex.Header.Width,
                    Height = tex.Header.Height,
                    MipLevels = (uint) numMipmaps,
                    ArraySize = isCubeMap ? 6u : 1u,
                    Format = format,
                    SampleDesc = new(1, 0),
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = isCubeMap ? (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_TEXTURECUBE : 0u,
                };
                pDevice->CreateTexture2D(&textureDesc, pSubresources, (ID3D11Texture2D**) &pResource).Ensure();
            } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
                var textureDesc = new D3D11_TEXTURE3D_DESC {
                    Width = tex.Header.Width,
                    Height = tex.Header.Height,
                    Depth = tex.Header.Depth,
                    MipLevels = (uint) numMipmaps,
                    Format = format,
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                    BindFlags = (uint) D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                pDevice->CreateTexture3D(&textureDesc, pSubresources, (ID3D11Texture3D**) &pResource).Ensure();
            } else
                throw new NotSupportedException();
        }

        return pResource;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture1D* pResource,
        ID3D11Device* pDevice)
    {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC();

        var desc = new D3D11_TEXTURE1D_DESC();
        pResource->GetDesc(&desc);
        if (desc.ArraySize == 1) {
            shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE1D;
            shaderViewDesc.Anonymous.Texture1D = new() { MostDetailedMip = 0u, MipLevels = desc.MipLevels };
        } else {
            shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE1DARRAY;
            shaderViewDesc.Anonymous.Texture1DArray = new() {
                MostDetailedMip = 0u,
                MipLevels = desc.MipLevels,
                FirstArraySlice = 0u,
                ArraySize = desc.ArraySize,
            };
        }

        pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView).Ensure();
        return pResourceView;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture2D* pResource,
        ID3D11Device* pDevice
    )
    {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC();

        var desc = new D3D11_TEXTURE2D_DESC();
        pResource->GetDesc(&desc);
        if ((desc.MiscFlags & (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_TEXTURECUBE) == 0) {
            if (desc.ArraySize == 1) {
                shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2D;
                shaderViewDesc.Anonymous.Texture2D = new() { MostDetailedMip = 0u, MipLevels = desc.MipLevels };
            } else {
                shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
                shaderViewDesc.Anonymous.Texture2DArray = new() {
                    MostDetailedMip = 0u,
                    MipLevels = desc.MipLevels,
                    FirstArraySlice = 0u,
                    ArraySize = desc.ArraySize,
                };
            }
        } else {
            if (desc.ArraySize == 6) {
                shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURECUBE;
                shaderViewDesc.Anonymous.TextureCube = new() { MostDetailedMip = 0u, MipLevels = desc.MipLevels };
            } else {
                shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURECUBEARRAY;
                shaderViewDesc.Anonymous.TextureCubeArray = new() {
                    MostDetailedMip = 0u,
                    MipLevels = desc.MipLevels,
                    First2DArrayFace = 0u,
                    NumCubes = desc.ArraySize / 6,
                };
            }
        }

        pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView).Ensure();
        return pResourceView;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture3D* pResource,
        ID3D11Device* pDevice
    )
    {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new D3D11_SHADER_RESOURCE_VIEW_DESC();

        var desc = new D3D11_TEXTURE3D_DESC();
        pResource->GetDesc(&desc);
        shaderViewDesc.ViewDimension = D3D_SRV_DIMENSION.D3D11_SRV_DIMENSION_TEXTURE3D;
        shaderViewDesc.Anonymous.Texture3D = new() { MostDetailedMip = 0u, MipLevels = desc.MipLevels };
        ;
        pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView).Ensure();
        return pResourceView;
    }
}
