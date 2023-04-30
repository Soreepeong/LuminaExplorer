using System;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util.DdsStructs;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff;

public static unsafe class ResourceUtils {
    public static byte[] CompileShaderFromAssemblyResource(
        this Type typeSharingNamespace,
        string target,
        string entrypointName = "main",
        string? fileName = null) {
        byte[] buffer;
        fileName ??= $"{typeSharingNamespace.Name}.hlsl";
        using (var stream = typeSharingNamespace.Assembly
                   .GetManifestResourceStream($"{typeSharingNamespace.Namespace}.{fileName}")!)
            stream.ReadExactly(buffer = new byte[stream.Length]);

        ID3D10Blob* pCode = null;
        ID3D10Blob* pErrorMsgs = null;
        try {
            fixed (void* pTarget = Encoding.UTF8.GetBytes(target))
            fixed (void* pEntrypointName = Encoding.UTF8.GetBytes(entrypointName))
            fixed (byte* pBuffer = &buffer[0]) {
                
                var hr = D3DCompiler.GetApi().Compile(
                    pBuffer,
                    (nuint) buffer.Length,
                    (byte*) null,
                    null,
                    null,
                    (byte*) pEntrypointName,
                    (byte*) pTarget,
                    1 | 2, // debug | skip_optimization
                    0,
                    &pCode,
                    &pErrorMsgs);

                if (hr < 0) {
                    if (pErrorMsgs is not null)
                        throw new(Encoding.UTF8.GetString(pErrorMsgs->Buffer));
                    Marshal.ThrowExceptionForHR(hr);
                }
            }

            buffer = new byte[pCode->Buffer.Length];
            pCode->Buffer.CopyTo(new(buffer));
            return buffer;
        } finally {
            if (pCode is not null)
                pCode->Release();
            if (pErrorMsgs is not null)
                pErrorMsgs->Release();
        }
    }

    public static T* CreateD3DTextureResource<T>(this DdsFile dds, ID3D11Device* pDevice)
        where T : unmanaged {
        T* pResource = null;
        var numImages = (uint) dds.NumImages;
        var numFaces = dds.IsCubeMap ? 6u : 1u;
        var numMipmaps = (uint) dds.NumMipmaps;

        var format = (Format) dds.PixFmt.DxgiFormat;
        if (format == Format.FormatUnknown)
            throw new NotSupportedException("Not a supported DXGI format");

        var subresources = new SubresourceData[numImages, numFaces, numMipmaps];
        fixed (void* b = dds.Data)
        fixed (SubresourceData* pSubresources = subresources) {
            for (var i = 0; i < numImages; i++) {
                for (var j = 0; j < numFaces; j++) {
                    for (var k = 0; k < numMipmaps; k++) {
                        subresources[i, j, k] = new(
                            pSysMem: (byte*) b + dds.MipmapDataOffset(i, j, k, out _) - dds.DataOffset,
                            sysMemPitch: (uint) dds.Pitch(k),
                            sysMemSlicePitch: (uint) dds.SliceSize(k));
                    }
                }
            }

            if (dds.Is1D) {
                var textureDesc = new Texture1DDesc {
                    Width = (uint) dds.Width(0),
                    MipLevels = numMipmaps,
                    ArraySize = numImages,
                    Format = format,
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                ThrowH(pDevice->CreateTexture1D(&textureDesc, pSubresources, (ID3D11Texture1D**) &pResource));
            } else if (dds.Is2D || dds.IsCubeMap) {
                var textureDesc = new Texture2DDesc {
                    Width = (uint) dds.Width(0),
                    Height = (uint) dds.Height(0),
                    MipLevels = numMipmaps,
                    ArraySize = numImages * (dds.IsCubeMap ? 6u : 1u),
                    Format = format,
                    SampleDesc = new(1, 0),
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = dds.IsCubeMap ? (uint) ResourceMiscFlag.Texturecube : 0u,
                };
                ThrowH(pDevice->CreateTexture2D(&textureDesc, pSubresources, (ID3D11Texture2D**) &pResource));
            } else if (dds.Is3D) {
                if (numImages != 1)
                    throw new NotSupportedException("3D Textures can only have 1 image.");
                var textureDesc = new Texture3DDesc {
                    Width = (uint) dds.Width(0),
                    Height = (uint) dds.Height(0),
                    Depth = (uint) dds.Depth(0),
                    MipLevels = numMipmaps,
                    Format = format,
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                ThrowH(pDevice->CreateTexture3D(&textureDesc, pSubresources, (ID3D11Texture3D**) &pResource));
            } else
                throw new NotSupportedException();
        }

        return pResource;
    }

    public static T* CreateD3DTextureResource<T>(this TexFile tex, ID3D11Device* pDevice)
        where T : unmanaged {
        T* pResource = null;
        var isCubeMap = tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube);
        var numFaces = isCubeMap ? 6u : 1u;
        var numMipmaps = tex.Header.MipLevels;

        var (formatInt, conversion) = TexFile.GetDxgiFormatFromTextureFormat(tex.Header.Format);
        var format = (Format) formatInt;
        var buffer = tex.TextureBuffer;
        switch (conversion) {
            case TexFile.DxgiFormatConversion.NoConversion:
                break;
            case TexFile.DxgiFormatConversion.FromL8ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB4G4R4A4ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB5G5R5A1ToB8G8R8A8:
                buffer = buffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
                format = Format.FormatB8G8R8A8Unorm;
                break;
            default:
                throw new NotSupportedException();
        }

        var subresources = new SubresourceData[numFaces, numMipmaps];
        fixed (void* b = buffer.RawData)
        fixed (SubresourceData* pSubresources = subresources) {
            var bufferOffset = 0u;
            for (var j = 0; j < numFaces; j++) {
                for (var k = 0; k < numMipmaps; k++) {
                    var mipHeight = (uint) buffer.HeightOfMipmap(k);
                    var slicePitch = (uint) buffer.NumBytesOfMipmapPerPlane(k);
                    var depth = (uint) buffer.DepthOfMipmap(k);
                    var pitch = slicePitch / mipHeight;
                    if (pitch * mipHeight != slicePitch)
                        throw new NotSupportedException("pitch * height != slicePitch?");

                    subresources[j, k] = new((byte*) b + bufferOffset, pitch, slicePitch);
                    bufferOffset += slicePitch * depth;
                }
            }

            if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType1D)) {
                var textureDesc = new Texture1DDesc {
                    Width = tex.Header.Width,
                    MipLevels = numMipmaps,
                    ArraySize = 1,
                    Format = format,
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                ThrowH(pDevice->CreateTexture1D(&textureDesc, pSubresources, (ID3D11Texture1D**) &pResource));
            } else if (
                tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D) ||
                isCubeMap) {
                var textureDesc = new Texture2DDesc {
                    Width = tex.Header.Width,
                    Height = tex.Header.Height,
                    MipLevels = numMipmaps,
                    ArraySize = isCubeMap ? 6u : 1u,
                    Format = format,
                    SampleDesc = new(1, 0),
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = isCubeMap ? (uint) ResourceMiscFlag.Texturecube : 0u,
                };
                ThrowH(pDevice->CreateTexture2D(&textureDesc, pSubresources, (ID3D11Texture2D**) &pResource));
            } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
                var textureDesc = new Texture3DDesc {
                    Width = tex.Header.Width,
                    Height = tex.Header.Height,
                    Depth = tex.Header.Depth,
                    MipLevels = numMipmaps,
                    Format = format,
                    Usage = Usage.Default,
                    BindFlags = (uint) BindFlag.ShaderResource,
                    CPUAccessFlags = 0,
                    MiscFlags = 0u,
                };
                ThrowH(pDevice->CreateTexture3D(&textureDesc, pSubresources, (ID3D11Texture3D**) &pResource));
            } else
                throw new NotSupportedException();
        }

        return pResource;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture1D* pResource,
        ID3D11Device* pDevice) {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new ShaderResourceViewDesc();

        var desc = new Texture1DDesc();
        pResource->GetDesc(&desc);
        if (desc.ArraySize == 1) {
            shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexture1D;
            shaderViewDesc.Anonymous.Texture1D = new(0u, desc.MipLevels);
        } else {
            shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexture1Darray;
            shaderViewDesc.Anonymous.Texture1DArray = new(0u, desc.MipLevels, 0u, desc.ArraySize);
        }

        ThrowH(pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView));
        return pResourceView;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture2D* pResource,
        ID3D11Device* pDevice
    ) {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new ShaderResourceViewDesc();

        var desc = new Texture2DDesc();
        pResource->GetDesc(&desc);
        if ((desc.MiscFlags & (uint) ResourceMiscFlag.Texturecube) == 0) {
            if (desc.ArraySize == 1) {
                shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexture2D;
                shaderViewDesc.Anonymous.Texture2D = new(0u, desc.MipLevels);
            } else {
                shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexture2Darray;
                shaderViewDesc.Anonymous.Texture2DArray = new(0u, desc.MipLevels, 0u, desc.ArraySize);
            }
        } else {
            if (desc.ArraySize == 6) {
                shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexturecube;
                shaderViewDesc.Anonymous.TextureCube = new(0u, desc.MipLevels);
            } else {
                shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexturecubearray;
                shaderViewDesc.Anonymous.TextureCubeArray = new(0u, desc.MipLevels, 0u, desc.ArraySize / 6);
            }
        }

        ThrowH(pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView));
        return pResourceView;
    }

    public static ID3D11ShaderResourceView* CreateShaderResourceView(
        ID3D11Texture3D* pResource,
        ID3D11Device* pDevice
    ) {
        ID3D11ShaderResourceView* pResourceView = null;

        var shaderViewDesc = new ShaderResourceViewDesc();

        var desc = new Texture3DDesc();
        pResource->GetDesc(&desc);
        shaderViewDesc.ViewDimension = D3DSrvDimension.D3D11SrvDimensionTexture3D;
        shaderViewDesc.Anonymous.Texture3D = new(0u, desc.MipLevels);
        ThrowH(pDevice->CreateShaderResourceView((ID3D11Resource*) pResource, &shaderViewDesc, &pResourceView));
        return pResourceView;
    }

    private static void ThrowH(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    private static void SafeRelease<T>(ref T* u) where T : unmanaged {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
    }
}
