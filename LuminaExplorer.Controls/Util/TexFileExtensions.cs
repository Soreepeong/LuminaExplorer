using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DirectN;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;

namespace LuminaExplorer.Controls.Util;

public static class TexFileExtensions {
    public static MemoryStream ToDds(this TexFile texFile) {
        var (dxgiFormat, conv) = TexFile.GetDxgiFormatFromTextureFormat(texFile.Header.Format);
        TextureBuffer texBuf;
        switch (conv) {
            case TexFile.DxgiFormatConversion.FromL8ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB4G4R4A4ToB8G8R8A8:
            case TexFile.DxgiFormatConversion.FromB5G5R5A1ToB8G8R8A8:
                texBuf = texFile.TextureBuffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
                break;
            default:
                texBuf = texFile.TextureBuffer;
                break;
        }

        var header = new DdsHeaderDxt10Whole {
            Magic = 0x20534444,
            Header = new() {
                Size = Unsafe.SizeOf<DdsHeader>(),
                Flags = DdsHeaderFlags.Caps |
                        DdsHeaderFlags.Height |
                        DdsHeaderFlags.Width |
                        DdsHeaderFlags.PixelFormat |
                        DdsHeaderFlags.MipmapCount |
                        DdsHeaderFlags.Depth,
                Height = texFile.Header.Height,
                Width = texFile.Header.Width,
                Depth = texFile.Header.Depth,
                MipMapCount = texFile.Header.MipLevels,
                Caps = 0x401008, // Complex | Mipmap | Texture
                Caps2 = (texFile.Header.Type & TexFile.Attribute.TextureTypeCube) != 0 ? 0xFE00 : 0,
                PixelFormat = new() {
                    Size = Unsafe.SizeOf<DdsPixelFormat>(),
                    Flags = 4, // FourCC
                    FourCC = 0x30315844, // 'DX10'
                }
            },
            Header10 = new() {
                DxgiFormat = (DXGI_FORMAT) dxgiFormat,
                ResourceDimension =
                    (texFile.Header.Type & TexFile.Attribute.TextureType1D) != 0
                        ?
                        D3D10ResourceDimension.D3D10_RESOURCE_DIMENSION_TEXTURE1D
                        :
                        (texFile.Header.Type & (TexFile.Attribute.TextureType2D | TexFile.Attribute.TextureTypeCube)) !=
                        0
                            ? D3D10ResourceDimension.D3D10_RESOURCE_DIMENSION_TEXTURE2D
                            :
                            (texFile.Header.Type & TexFile.Attribute.TextureType3D) != 0
                                ? D3D10ResourceDimension.D3D10_RESOURCE_DIMENSION_TEXTURE3D
                                : 0,
                MiscFlag = (texFile.Header.Type & TexFile.Attribute.TextureTypeCube) != 0 ? 4 : 0,
                ArraySize = texFile.Header.Depth *
                            ((texFile.Header.Type & TexFile.Attribute.TextureTypeCube) != 0 ? 6 : 1),
                MiscFlags2 = 1, // straight
            },
        };
        var res = new MemoryStream(Unsafe.SizeOf<DdsHeaderDxt10Whole>() + texBuf.RawData.Length);
        unsafe {
            res.Write(new(&header, Unsafe.SizeOf<DdsHeaderDxt10Whole>()));
        }

        res.Write(texBuf.RawData);
        res.Position = 0;
        return res;
    }

    [Flags]
    public enum DdsHeaderFlags {
        Caps = 0x1,
        Height = 0x2,
        Width = 0x4,
        Pitch = 0x8,
        PixelFormat = 0x1000,
        MipmapCount = 0x20000,
        LinearSize = 0x80000,
        Depth = 0x800000,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DdsPixelFormat {
        public int Size;
        public int Flags;
        public int FourCC;
        public int RghBitCount;
        public int RBitMask;
        public int GBitMask;
        public int BBitMask;
        public int ABitMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DdsHeader {
        public int Size;
        public DdsHeaderFlags Flags;
        public int Height;
        public int Width;
        public int PitchOrLinearSize;
        public int Depth;
        public int MipMapCount;
        public fixed int Reserved1[11];
        public DdsPixelFormat PixelFormat;
        public int Caps;
        public int Caps2;
        public int Caps3;
        public int Caps4;
        public int Reserved2;
    }

    public enum D3D10ResourceDimension {
        D3D10_RESOURCE_DIMENSION_UNKNOWN = 0,
        D3D10_RESOURCE_DIMENSION_BUFFER = 1,
        D3D10_RESOURCE_DIMENSION_TEXTURE1D = 2,
        D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3,
        D3D10_RESOURCE_DIMENSION_TEXTURE3D = 4
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct DdsHeaderDxt10 {
        public DXGI_FORMAT DxgiFormat;
        public D3D10ResourceDimension ResourceDimension;
        public int MiscFlag;
        public int ArraySize;
        public int MiscFlags2;
    };

    public struct DdsHeaderDxt10Whole {
        public int Magic;
        public DdsHeader Header;
        public DdsHeaderDxt10 Header10;
    }
}
