using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;
using ValueType = LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels.ValueType;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;

public class DdsFile {
    public readonly DdsHeaderLegacy LegacyHeader;
    public readonly bool UseDxt10Header;
    public readonly DdsHeaderDxt10 Dxt10Header;

    private readonly byte[] _data;

    public DdsFile(string name, DdsHeaderLegacy legacyHeader, DdsHeaderDxt10? dxt10Header, byte[] data)
    {
        this.Name = name;
        this.LegacyHeader = legacyHeader;
        this.UseDxt10Header = dxt10Header is not null;
        this.Dxt10Header = dxt10Header ?? new();
        this._data = data;
    }

    public DdsFile(string name, Stream stream, bool closeAfter = true)
    {
        this.Name = name;
        try {
            try {
                this._data = new byte[stream.Length];
                stream.ReadExactly(this._data);
            } catch (NotSupportedException) {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                this._data = ms.ToArray();
            }

            unsafe {
                fixed (void* lh = &this.LegacyHeader)
                    Marshal.Copy(this._data, 0, (nint) lh, sizeof(DdsHeaderLegacy));

                if (this.LegacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc) &&
                    this.LegacyHeader.Header.PixelFormat.FourCc == DdsFourCc.Dx10) {
                    this.UseDxt10Header = true;
                    fixed (void* dh = &this.Dxt10Header)
                        Marshal.Copy(this._data, sizeof(DdsHeaderLegacy), (nint) dh, sizeof(DdsHeaderDxt10));
                }
            }
        } finally {
            if (closeAfter)
                stream.Dispose();
        }
    }

    public DdsFile(string name, byte[] data)
    {
        this.Name = name;
        this._data = data;
        unsafe {
            fixed (void* lh = &this.LegacyHeader)
                Marshal.Copy(this._data, 0, (nint) lh, sizeof(DdsHeaderLegacy));

            if (this.LegacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc) &&
                this.LegacyHeader.Header.PixelFormat.FourCc == DdsFourCc.Dx10) {
                this.UseDxt10Header = true;
                fixed (void* dh = &this.Dxt10Header)
                    Marshal.Copy(this._data, sizeof(DdsHeaderLegacy), (nint) dh, sizeof(DdsHeaderDxt10));
            }
        }
    }

    public string Name { get; }

    public int DataOffset =>
        Unsafe.SizeOf<DdsHeaderLegacy>() +
        (this.UseDxt10Header ? Unsafe.SizeOf<DdsHeaderDxt10>() : 0);

    public Stream CreateStream() => new MemoryStream(this._data, false);

    public DdsHeader Header => this.LegacyHeader.Header;

    public ReadOnlySpan<byte> Body => new(this._data, this.DataOffset, this._data.Length - this.DataOffset);

    public ReadOnlySpan<byte> Data => this._data.AsSpan();

    public int NumImages => this.UseDxt10Header ? this.Dxt10Header.ArraySize : 1;

    public int NumMipmaps => this.Header.Flags.HasFlag(DdsHeaderFlags.MipmapCount) ? this.Header.MipMapCount : 1;

    public int Bpp => this.PixFmt.Bpp;

    public bool Is1D =>
        !this.IsCubeMap && (this.Header.Flags & DdsHeaderFlags.DimensionMask) == DdsHeaderFlags.Dimension1;

    public bool Is2D =>
        !this.IsCubeMap && (this.Header.Flags & DdsHeaderFlags.DimensionMask) == DdsHeaderFlags.Dimension2;

    public bool Is3D =>
        !this.IsCubeMap && (this.Header.Flags & DdsHeaderFlags.DimensionMask) == DdsHeaderFlags.Dimension3;

    public bool IsCubeMap => this.Header.Caps2.HasFlag(DdsCaps2.Cubemap);

    public int Width(int mipmapIndex) =>
        0 <= mipmapIndex && mipmapIndex < this.NumMipmaps
            ? this.Header.Flags.HasFlag(DdsHeaderFlags.Width) ? Math.Max(1, this.Header.Width >> mipmapIndex) : 1
            : throw new ArgumentOutOfRangeException(nameof(mipmapIndex), mipmapIndex, null);

    public int Pitch(int mipmapIndex)
    {
        var pf = this.PixFmt;
        if (pf is BcPixFmt bcPixelFormat)
            return Math.Max(1, (this.Width(mipmapIndex) + 3) / 4) * bcPixelFormat.BlockSize;

        // For R8G8_B8G8, G8R8_G8B8, legacy UYVY-packed, and legacy YUY2-packed formats, compute the pitch as:
        // ((width+1) >> 1) * 4

        return (this.Width(mipmapIndex) * pf.Bpp + 7) / 8;
    }

    public int Height(int mipmapIndex) =>
        0 <= mipmapIndex && mipmapIndex < this.NumMipmaps
            ? this.Header.Flags.HasFlag(DdsHeaderFlags.Height) ? Math.Max(1, this.Header.Height >> mipmapIndex) : 1
            : throw new ArgumentOutOfRangeException(nameof(mipmapIndex), mipmapIndex, null);

    public int NumFaces => !this.IsCubeMap
        ? 1
        : (this.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeX) ? 1 : 0)
        + (this.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveX) ? 1 : 0)
        + (this.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeY) ? 1 : 0)
        + (this.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveY) ? 1 : 0)
        + (this.Header.Caps2.HasFlag(DdsCaps2.CubemapNegativeZ) ? 1 : 0)
        + (this.Header.Caps2.HasFlag(DdsCaps2.CubemapPositiveZ) ? 1 : 0);

    public int Depth(int mipmapIndex) => 0 <= mipmapIndex && mipmapIndex < this.NumMipmaps
        ? this.Header.Flags.HasFlag(DdsHeaderFlags.Depth) ? Math.Max(1, this.Header.Depth >> mipmapIndex) : 1
        : throw new ArgumentOutOfRangeException(nameof(mipmapIndex), mipmapIndex, null);

    public int DepthOrNumFaces(int mipmapIndex) => this.IsCubeMap ? this.NumFaces : this.Depth(mipmapIndex);

    public int SliceSize(int mipmapIndex)
    {
        var pf = this.PixFmt;
        if (pf is BcPixFmt bcPixelFormat) {
            return Math.Max(1, (this.Width(mipmapIndex) + 3) / 4) *
                Math.Max(1, (this.Height(mipmapIndex) + 3) / 4) *
                bcPixelFormat.BlockSize;
        }

        // For R8G8_B8G8, G8R8_G8B8, legacy UYVY-packed, and legacy YUY2-packed formats, compute the pitch as:
        // ((width+1) >> 1) * 4

        return (this.Width(mipmapIndex) * pf.Bpp + 7) / 8 * this.Height(mipmapIndex);
    }

    public int MipmapSize(int mipmapIndex) => this.SliceSize(mipmapIndex) * this.Depth(mipmapIndex);

    public int FaceSize => Enumerable.Range(0, this.NumMipmaps).Sum(this.MipmapSize);

    public int ImageSize => this.FaceSize * this.NumFaces;

    public int ImageDataOffset(int imageIndex, out int size)
    {
        if (imageIndex < 0 || imageIndex >= this.NumImages)
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, null);

        size = this.ImageSize;
        return this.DataOffset + size * imageIndex;
    }

    public ReadOnlySpan<byte> ImageData(int imageIndex)
    {
        var offset = this.ImageDataOffset(imageIndex, out var size);
        return new(this._data, offset, size);
    }

    public int FaceDataOffset(int imageIndex, int faceIndex, out int size)
    {
        var offset = this.ImageDataOffset(imageIndex, out _);
        size = this.FaceSize;
        return offset + size * faceIndex;
    }

    public ReadOnlySpan<byte> FaceData(int imageIndex, int faceIndex)
    {
        var offset = this.FaceDataOffset(imageIndex, faceIndex, out var size);
        return new(this._data, offset, size);
    }

    public int MipmapDataOffset(int imageIndex, int faceIndex, int mipmapIndex, out int size)
    {
        var baseOffset = this.FaceDataOffset(imageIndex, faceIndex, out _);
        var mipOffset = Enumerable.Range(0, mipmapIndex).Sum(this.MipmapSize);
        size = this.MipmapSize(mipmapIndex);
        return baseOffset + mipOffset;
    }

    public ReadOnlySpan<byte> MipmapData(int imageIndex, int faceIndex, int mipmapIndex)
    {
        var offset = this.MipmapDataOffset(imageIndex, faceIndex, mipmapIndex, out var size);
        return new(this._data, offset, size);
    }

    public int SliceDataOffset(int imageIndex, int faceIndex, int mipmapIndex, int sliceIndex, out int size)
    {
        var offset = this.MipmapDataOffset(imageIndex, faceIndex, mipmapIndex, out _);
        size = this.SliceSize(mipmapIndex);
        return offset + size * sliceIndex;
    }

    public ReadOnlySpan<byte> SliceData(int imageIndex, int faceIndex, int mipmapIndex, int sliceIndex)
    {
        var offset = this.SliceDataOffset(imageIndex, faceIndex, mipmapIndex, sliceIndex, out var size);
        return new(this._data, offset, size);
    }

    public int SliceOrFaceDataOffset(int imageIndex, int mipmapIndex, int sliceIndex, out int size) =>
        this.IsCubeMap
            ? this.SliceDataOffset(imageIndex, sliceIndex, mipmapIndex, 0, out size)
            : this.SliceDataOffset(imageIndex, 0, mipmapIndex, sliceIndex, out size);

    public ReadOnlySpan<byte> SliceOrFaceData(int imageIndex, int mipmapIndex, int sliceIndex) =>
        this.IsCubeMap
            ? this.SliceData(imageIndex, sliceIndex, mipmapIndex, 0)
            : this.SliceData(imageIndex, 0, mipmapIndex, sliceIndex);

    public IPixFmt PixFmt {
        get {
            var pf = this.Header.PixelFormat;

            if (!pf.Flags.HasFlag(DdsPixelFormatFlags.FourCc)) {
                var alpha = ChannelDefinition.Empty;

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels))
                    alpha = ChannelDefinition.FromMask(ValueType.Unorm, pf.ABitMask);

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Rgb)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~(pf.RBitMask | pf.GBitMask | pf.BBitMask) &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new RgbaPixFmt(
                        alpha.IsEmpty ? AlphaType.None : AlphaType.Straight,
                        r: ChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        g: ChannelDefinition.FromMask(ValueType.Unorm, pf.GBitMask),
                        b: ChannelDefinition.FromMask(ValueType.Unorm, pf.BBitMask),
                        a: alpha,
                        x1: ChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Yuv)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~(pf.RBitMask | pf.GBitMask | pf.BBitMask) &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new YuvPixFmt(
                        alpha.IsEmpty ? AlphaType.None : AlphaType.Straight,
                        y: ChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        u: ChannelDefinition.FromMask(ValueType.Unorm, pf.GBitMask),
                        v: ChannelDefinition.FromMask(ValueType.Unorm, pf.BBitMask),
                        a: alpha,
                        x: ChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Luminance)) {
                    var xbitmask =
                        unchecked((1u << pf.RgbBitCount) - 1u) & ~pf.RBitMask &
                        (pf.Flags.HasFlag(DdsPixelFormatFlags.AlphaPixels) ? ~pf.ABitMask : ~0u);
                    return new LumiPixFmt(
                        alpha.IsEmpty ? AlphaType.None : AlphaType.Straight,
                        l: ChannelDefinition.FromMask(ValueType.Unorm, pf.RBitMask),
                        a: alpha,
                        x: ChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                if (pf.Flags.HasFlag(DdsPixelFormatFlags.Alpha)) {
                    var xbitmask = unchecked((1u << pf.RgbBitCount) - 1u) & ~pf.ABitMask;
                    return new RgbaPixFmt(
                        AlphaType.Straight,
                        a: alpha,
                        x1: ChannelDefinition.FromMask(ValueType.Typeless, xbitmask));
                }

                return UnknownPixFmt.Instance;
            }

            var ipf = PixFmtResolver.GetPixelFormat(pf.FourCc);
            if (!Equals(ipf, UnknownPixFmt.Instance))
                return ipf;

            if (pf.FourCc != DdsFourCc.Dx10 || !this.UseDxt10Header)
                return UnknownPixFmt.Instance;

            return PixFmtResolver.GetPixelFormat(
                this.Dxt10Header.MiscFlags2 switch {
                    DdsHeaderDxt10MiscFlags2.AlphaModeUnknown => AlphaType.Straight,
                    DdsHeaderDxt10MiscFlags2.AlphaModeStraight => AlphaType.Straight,
                    DdsHeaderDxt10MiscFlags2.AlphaModePremultiplied => AlphaType.Premultiplied,
                    DdsHeaderDxt10MiscFlags2.AlphaModeOpaque => AlphaType.None,
                    DdsHeaderDxt10MiscFlags2.AlphaModeCustom => AlphaType.Custom,
                    _ => throw new ArgumentOutOfRangeException(),
                },
                this.Dxt10Header.DxgiFormat);
        }
    }
}
