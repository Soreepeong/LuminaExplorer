using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util.DdsStructs.PixelFormats;
using ValueType = LuminaExplorer.Core.Util.DdsStructs.PixelFormats.ValueType;

namespace LuminaExplorer.Core.Util.DdsStructs;

public class DdsFile {
    public readonly DdsHeaderLegacy LegacyHeader;
    public readonly bool UseDxt10Header;
    public readonly DdsHeaderDxt10 Dxt10Header;
    private readonly MemoryStream _stream;

    public DdsFile(string name, Stream stream, bool closeAfter = true) {
        Name = name;
        try {
            try {
                _stream = new(new byte[stream.Length]);
            } catch (NotSupportedException) {
                _stream = new();
            }

            try {
                stream.CopyTo(_stream);
            } catch (Exception) {
                _stream.Dispose();
                throw;
            }

            var br = new LuminaBinaryReader(_stream);
            LegacyHeader = br.ReadStructure<DdsHeaderLegacy>();
            if (LegacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc)) {
                UseDxt10Header = true;
                Dxt10Header = br.ReadStructure<DdsHeaderDxt10>();
            }
        } finally {
            if (closeAfter)
                stream.Dispose();
        }
    }

    public DdsFile(string name, TexFile tex) : this(
        name,
        tex,
        TexFile.TextureFormat.L8,
        TexFile.TextureFormat.B4G4R4A4,
        TexFile.TextureFormat.B5G5R5A1
    ) { }

    public DdsFile(string name, TexFile tex, params TexFile.TextureFormat[] formatsToConvertToB8G8R8A8) {
        Name = name;
        var texFormat = tex.Header.Format;
        var texBuf = tex.TextureBuffer;
        var (dxgiFormat, _) = TexFile.GetDxgiFormatFromTextureFormat(texFormat, false);
        if (formatsToConvertToB8G8R8A8.Contains(texFormat)) {
            texFormat = TexFile.TextureFormat.B8G8R8A8;
            texBuf = tex.TextureBuffer.Filter(format: texFormat);
        }

        LegacyHeader = new() {
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
            LegacyHeader.Header.Caps |= DdsCaps1.Complex | DdsCaps1.Mipmap;
            LegacyHeader.Header.Flags |= DdsHeaderFlags.MipmapCount;
            LegacyHeader.Header.MipMapCount = tex.Header.MipLevels;
        }

        if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
            LegacyHeader.Header.Caps |= DdsCaps1.Complex;
            LegacyHeader.Header.Flags |= DdsHeaderFlags.Depth;
            LegacyHeader.Header.Depth = tex.Header.Depth;
        } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube)) {
            LegacyHeader.Header.Caps |= DdsCaps1.Complex;
            LegacyHeader.Header.Caps2 |= DdsCaps2.AllFaces;
        }

        var type = (TexFile.TextureFormat) (
            (int) (texFormat & TexFile.TextureFormat.TypeMask) >>
            (int) TexFile.TextureFormat.TypeShift);
        var bpp = 1 << (
            (int) (texFormat & TexFile.TextureFormat.BppMask) >>
            (int) TexFile.TextureFormat.BppShift);

        if (type is TexFile.TextureFormat.TypeBc123 or TexFile.TextureFormat.TypeBc57)
            LegacyHeader.Header.LinearSize =
                Math.Max(1, (tex.Header.Width + 3) / 4) *
                Math.Max(1, (tex.Header.Height + 3) / 4) *
                (texFormat is TexFile.TextureFormat.BC1 ? 8 : 16);
        else
            LegacyHeader.Header.Pitch = bpp / 8 * tex.Header.Width;

        if (type is TexFile.TextureFormat.TypeInteger or TexFile.TextureFormat.TypeSpecial)
            LegacyHeader.Header.PixelFormat.RgbBitCount = bpp;

        switch (texFormat) {
            case TexFile.TextureFormat.BC1:
            case TexFile.TextureFormat.BC2:
            case TexFile.TextureFormat.BC3:
            case TexFile.TextureFormat.BC5:
            case TexFile.TextureFormat.BC7:
                // https://learn.microsoft.com/en-us/windows/win32/direct3d11/texture-block-compression-in-direct3d-11
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.FourCc;
                LegacyHeader.Header.PixelFormat.FourCc = texFormat switch {
                    TexFile.TextureFormat.BC1 => DdsFourCc.Bc1,
                    TexFile.TextureFormat.BC2 => DdsFourCc.Bc2,
                    TexFile.TextureFormat.BC3 => DdsFourCc.Bc3,
                    TexFile.TextureFormat.BC5 => DdsFourCc.Dx10, // Using "ATI2" FourCC is not recommended
                    TexFile.TextureFormat.BC7 => DdsFourCc.Dx10, // Empty "Direct3D 9 equivalent format"
                    _ => throw new NotSupportedException(),
                };
                break;
            case TexFile.TextureFormat.L8:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Luminance;
                // dwRBitMask contains the channel mask.
                LegacyHeader.Header.PixelFormat.RBitMask = 0xFF;
                break;
            case TexFile.TextureFormat.A8:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Alpha | DdsPixelFormatFlags.AlphaPixels;
                // dwABitMask contains valid data.
                LegacyHeader.Header.PixelFormat.ABitMask = 0xFF;
                break;
            case TexFile.TextureFormat.B4G4R4A4:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                LegacyHeader.Header.PixelFormat.BBitMask = 0xF;
                LegacyHeader.Header.PixelFormat.GBitMask = 0xF0;
                LegacyHeader.Header.PixelFormat.RBitMask = 0xF00;
                LegacyHeader.Header.PixelFormat.ABitMask = 0xF000;
                break;
            case TexFile.TextureFormat.B5G5R5A1:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                LegacyHeader.Header.PixelFormat.BBitMask = 0b0000000000011111;
                LegacyHeader.Header.PixelFormat.GBitMask = 0b0000001111100000;
                LegacyHeader.Header.PixelFormat.RBitMask = 0b0111110000000000;
                LegacyHeader.Header.PixelFormat.ABitMask = 0b1000000000000000;
                break;
            case TexFile.TextureFormat.B8G8R8A8:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb | DdsPixelFormatFlags.AlphaPixels;
                LegacyHeader.Header.PixelFormat.BBitMask = 0x000000FF;
                LegacyHeader.Header.PixelFormat.GBitMask = 0x0000FF00;
                LegacyHeader.Header.PixelFormat.RBitMask = 0x00FF0000;
                LegacyHeader.Header.PixelFormat.ABitMask = 0xFF000000;
                break;
            case TexFile.TextureFormat.B8G8R8X8:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                LegacyHeader.Header.PixelFormat.BBitMask = 0x000000FF;
                LegacyHeader.Header.PixelFormat.GBitMask = 0x0000FF00;
                LegacyHeader.Header.PixelFormat.RBitMask = 0x00FF0000;
                break;
            case TexFile.TextureFormat.D16:
            case TexFile.TextureFormat.Shadow16:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                // dwRBitMask contains the channel mask.
                LegacyHeader.Header.PixelFormat.RBitMask = 0xFFFF;
                break;
            case TexFile.TextureFormat.D24S8:
            case TexFile.TextureFormat.Shadow24:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.Rgb;
                LegacyHeader.Header.PixelFormat.RBitMask = 0x00FFFFFF;
                LegacyHeader.Header.PixelFormat.GBitMask = 0xFF000000;
                break;
            case TexFile.TextureFormat.Null:
                LegacyHeader.Header.PixelFormat.Flags = 0;
                break;
            default:
                LegacyHeader.Header.PixelFormat.Flags = DdsPixelFormatFlags.FourCc;
                LegacyHeader.Header.PixelFormat.FourCc = DdsFourCc.Dx10;
                break;
        }

        UseDxt10Header =
            LegacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc) &&
            LegacyHeader.Header.PixelFormat.FourCc == DdsFourCc.Dx10;

        _stream = new(
            Unsafe.SizeOf<DdsHeaderLegacy>() +
            (UseDxt10Header ? Unsafe.SizeOf<DdsHeaderDxt10>() : 0) +
            texBuf.RawData.Length
        );

        unsafe {
            fixed (void* p = &LegacyHeader)
                _stream.Write(new(p, Unsafe.SizeOf<DdsHeaderLegacy>()));
        }

        if (UseDxt10Header) {
            Dxt10Header = new() {
                DxgiFormat = (DxgiFormat) dxgiFormat,
                ArraySize = 1,
                MiscFlags2 = DdsHeaderDxt10MiscFlags2.AlphaModeStraight,
            };

            if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                Dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture1D;
            else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                Dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture2D;
            else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureType3D)) {
                Dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture3D;
            } else if (tex.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube)) {
                Dxt10Header.ResourceDimension = DdsHeaderDxt10ResourceDimension.Texture2D;
                Dxt10Header.MiscFlag = DdxHeaderDxt10MiscFlags.TextureCube;
                Dxt10Header.ArraySize = tex.Header.Depth;
            }

            unsafe {
                fixed (void* p = &Dxt10Header)
                    _stream.Write(new(p, Unsafe.SizeOf<DdsHeaderDxt10>()));
            }
        }

        _stream.Write(texBuf.RawData);
    }

    public string Name { get; }

    public int DataOffset =>
        Unsafe.SizeOf<DdsHeaderLegacy>() +
        (UseDxt10Header ? Unsafe.SizeOf<DdsHeaderDxt10>() : 0);

    public Stream CreateStream() => new MemoryStream(_stream.GetBuffer(), false);

    public DdsHeader Header => LegacyHeader.Header;

    public ReadOnlySpan<byte> Data => new(_stream.GetBuffer(), DataOffset, _stream.GetBuffer().Length - DataOffset);

    // https://learn.microsoft.com/en-us/windows/win32/direct3d10/d3d10-graphics-programming-guide-resources-data-conversion
    public IPixelFormat PixelFormat {
        get {
            var pf = Header.PixelFormat;

            if (!pf.Flags.HasFlag(DdsPixelFormatFlags.FourCc)) {
                var alpha = new AlphaChannelDefinition();

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels))
                    alpha = AlphaChannelDefinition.FromMask(ValueType.Unorm, AlphaType.Straight, pf.ABitMask);

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Rgb)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~(pf.RBitMask | pf.GBitMask | pf.BBitMask) &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new RgbaxPixelFormat(
                        r: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        g: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.GBitMask),
                        b: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.BBitMask),
                        a: alpha,
                        x1: ColorChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Yuv)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~(pf.RBitMask | pf.GBitMask | pf.BBitMask) &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new YuvPixelFormat(
                        y: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        u: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.GBitMask),
                        v: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.BBitMask),
                        a: alpha,
                        x: ColorChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Luminance)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~pf.RBitMask &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new LuminancePixelFormat(
                        l: ColorChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        a: alpha,
                        x: ColorChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Alpha)) {
                    var xbitmask = unchecked((1u << pf.RgbBitCount) - 1u) & ~pf.ABitMask;
                    return new RgbaxPixelFormat(
                        a: alpha,
                        x1: ColorChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                return new UnknownPixelFormat();
            }

            var ipf = PixelFormatResolver.GetPixelFormat(pf.FourCc);
            if (!Equals(ipf, UnknownPixelFormat.Instance))
                return ipf;

            if (pf.FourCc != DdsFourCc.Dx10 || !UseDxt10Header)
                return UnknownPixelFormat.Instance;

            return PixelFormatResolver.GetPixelFormat(Dxt10Header.DxgiFormat);
        }
    }
}
