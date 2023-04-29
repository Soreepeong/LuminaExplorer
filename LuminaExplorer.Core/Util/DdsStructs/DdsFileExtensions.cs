using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Lumina.Data.Files;

namespace LuminaExplorer.Core.Util.DdsStructs;

public static class DdsFileExtensions {
    public static DdsFile ToDdsFileFollowGameDx11Conversion(this TexFile tex)
        => tex.ToDdsFile(
            TexFile.TextureFormat.L8,
            TexFile.TextureFormat.B4G4R4A4,
            TexFile.TextureFormat.B5G5R5A1);

    public static DdsFile ToDdsFile(this TexFile tex, params TexFile.TextureFormat[] formatsToConvertToB8G8R8A8) {
        var texFormat = tex.Header.Format;
        var texBuf = tex.TextureBuffer;
        var (dxgiFormat, _) = TexFile.GetDxgiFormatFromTextureFormat(texFormat, false);
        if (formatsToConvertToB8G8R8A8.Contains(texFormat)) {
            texFormat = TexFile.TextureFormat.B8G8R8A8;
            texBuf = tex.TextureBuffer.Filter(format: texFormat);
        }

        var legacyHeader = new DdsHeaderLegacy {
            Magic = DdsHeaderLegacy.MagicValue,
            Header = new() {
                Size = Unsafe.SizeOf<DdsHeader>(),
                Flags = DdsHeaderFlags.Caps |
                    DdsHeaderFlags.Height |
                    DdsHeaderFlags.Width |
                    DdsHeaderFlags.PixelFormat,
                Height = tex.Header.Height,
                Width = tex.Header.Width,
                Caps = DdsCaps1.Texture,
                PixelFormat = new() {
                    Size = Unsafe.SizeOf<DdsPixelFormat>(),
                }
            },
        };

        if (tex.Header.MipLevels > 1) {
            legacyHeader.Header.Caps |= DdsCaps1.Complex | DdsCaps1.Mipmap;
            legacyHeader.Header.Flags |= DdsHeaderFlags.MipmapCount;
            legacyHeader.Header.MipMapCount = tex.Header.MipLevels;
        }

        if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
            legacyHeader.Header.Caps |= DdsCaps1.Complex;
            legacyHeader.Header.Flags |= DdsHeaderFlags.Depth;
            legacyHeader.Header.Depth = tex.Header.Depth;
        } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube)) {
            legacyHeader.Header.Caps |= DdsCaps1.Complex;
            legacyHeader.Header.Caps2 |= DdsCaps2.AllFaces;
        }

        var type = (TexFile.TextureFormat) (
            (int) (texFormat & TexFile.TextureFormat.TypeMask) >>
            (int) TexFile.TextureFormat.TypeShift);
        var bpp = 1 << (
            (int) (texFormat & TexFile.TextureFormat.BppMask) >>
            (int) TexFile.TextureFormat.BppShift);

        if (type is TexFile.TextureFormat.TypeBc123 or TexFile.TextureFormat.TypeBc57)
            legacyHeader.Header.LinearSize =
                Math.Max(1, (tex.Header.Width + 3) / 4) *
                Math.Max(1, (tex.Header.Height + 3) / 4) *
                (texFormat is TexFile.TextureFormat.BC1 ? 8 : 16);
        else
            legacyHeader.Header.Pitch = bpp / 8 * tex.Header.Width;

        if (type is TexFile.TextureFormat.TypeInteger or TexFile.TextureFormat.TypeSpecial)
            legacyHeader.Header.PixelFormat.RgbBitCount = bpp;

        switch (texFormat) {
            case TexFile.TextureFormat.BC1:
            case TexFile.TextureFormat.BC2:
            case TexFile.TextureFormat.BC3:
            case TexFile.TextureFormat.BC5:
            case TexFile.TextureFormat.BC7:
                // https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.FourCc;
                legacyHeader.Header.PixelFormat.FourCc = texFormat switch {
                    TexFile.TextureFormat.BC1 => DdsFourCc.Bc1,
                    TexFile.TextureFormat.BC2 => DdsFourCc.Bc2,
                    TexFile.TextureFormat.BC3 => DdsFourCc.Bc3,
                    TexFile.TextureFormat.BC5 => DdsFourCc.Dx10, // Using "ATI2" FourCC is not recommended
                    TexFile.TextureFormat.BC7 => DdsFourCc.Dx10, // Empty "Direct3D 9 equivalent format"
                    _ => throw new NotSupportedException(),
                };
                break;
            case TexFile.TextureFormat.L8:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Luminance;
                // dwRBitMask contains the channel mask.
                legacyHeader.Header.PixelFormat.RBitMask = 0xFF;
                break;
            case TexFile.TextureFormat.A8:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Alpha | DdsPixelFormatFlags.AlphaPixels;
                // dwABitMask contains valid data.
                legacyHeader.Header.PixelFormat.ABitMask = 0xFF;
                break;
            case TexFile.TextureFormat.B4G4R4A4:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                legacyHeader.Header.PixelFormat.BBitMask = 0x000F;
                legacyHeader.Header.PixelFormat.GBitMask = 0x00F0;
                legacyHeader.Header.PixelFormat.RBitMask = 0x0F00;
                legacyHeader.Header.PixelFormat.ABitMask = 0xF000;
                break;
            case TexFile.TextureFormat.B5G5R5A1:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                legacyHeader.Header.PixelFormat.BBitMask = 0b0000000000011111;
                legacyHeader.Header.PixelFormat.GBitMask = 0b0000001111100000;
                legacyHeader.Header.PixelFormat.RBitMask = 0b0111110000000000;
                legacyHeader.Header.PixelFormat.ABitMask = 0b1000000000000000;
                break;
            case TexFile.TextureFormat.B8G8R8A8:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                legacyHeader.Header.PixelFormat.BBitMask = 0x000000FF;
                legacyHeader.Header.PixelFormat.GBitMask = 0x0000FF00;
                legacyHeader.Header.PixelFormat.RBitMask = 0x00FF0000;
                legacyHeader.Header.PixelFormat.ABitMask = 0xFF000000;
                break;
            case TexFile.TextureFormat.B8G8R8X8:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                legacyHeader.Header.PixelFormat.BBitMask = 0x000000FF;
                legacyHeader.Header.PixelFormat.GBitMask = 0x0000FF00;
                legacyHeader.Header.PixelFormat.RBitMask = 0x00FF0000;
                break;
            case TexFile.TextureFormat.D16:
            case TexFile.TextureFormat.Shadow16:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                // dwRBitMask contains the channel mask.
                legacyHeader.Header.PixelFormat.RBitMask = 0xFFFF;
                break;
            case TexFile.TextureFormat.D24S8:
            case TexFile.TextureFormat.Shadow24:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                legacyHeader.Header.PixelFormat.RBitMask = 0x00FFFFFF;
                legacyHeader.Header.PixelFormat.GBitMask = 0xFF000000;
                break;
            case TexFile.TextureFormat.Null:
                legacyHeader.Header.PixelFormat.Flags = 0;
                break;
            default:
                legacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.FourCc;
                legacyHeader.Header.PixelFormat.FourCc = DdsFourCc.Dx10;
                break;
        }

        var UseDxt10Header =
            legacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc) &&
            legacyHeader.Header.PixelFormat.FourCc == DdsFourCc.Dx10;

        var data = new byte[
            Unsafe.SizeOf<DdsHeaderLegacy>() +
            (UseDxt10Header ? Unsafe.SizeOf<DdsHeaderDxt10>() : 0) +
            texBuf.RawData.Length];
        using var stream = new MemoryStream(data);

        unsafe {
            stream.Write(new(&legacyHeader, Unsafe.SizeOf<DdsHeaderLegacy>()));
        }

        DdsHeaderDxt10 dxt10Header;
        if (UseDxt10Header) {
            dxt10Header = new() {
                DxgiFormat = (DxgiFormat) dxgiFormat,
                ArraySize = 1,
                MiscFlags2 = DdsHeaderDxt10MiscFlags2.AlphaModeStraight,
            };

            if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture1D;
            else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture2D;
            else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
                dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture3D;
            } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube)) {
                dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture2D;
                dxt10Header.MiscFlag = DdxHeaderDxt10MiscFlags.TextureCube;
                dxt10Header.ArraySize = tex.Header.Depth;
            }

            unsafe {
                stream.Write(new(&dxt10Header, Unsafe.SizeOf<DdsHeaderDxt10>()));
            }
        } else
            dxt10Header = new();

        stream.Write(texBuf.RawData);

        return new(
            Path.ChangeExtension(Path.GetFileName(tex.FilePath.Path), ".dds"),
            legacyHeader,
            UseDxt10Header ? dxt10Header : null,
            data);
    }
}
