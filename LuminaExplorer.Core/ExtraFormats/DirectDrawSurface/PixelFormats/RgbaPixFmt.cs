using System;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;
using ValueType = LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels.ValueType;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;

public class RgbaPixFmt : IPixFmt, IEquatable<RgbaPixFmt> {
    public RgbaPixFmt(
        AlphaType alphaType,
        ChannelDefinition? r = null,
        ChannelDefinition? g = null,
        ChannelDefinition? b = null,
        ChannelDefinition? a = null,
        ChannelDefinition? x1 = null,
        ChannelDefinition? x2 = null)
    {
        this.Alpha = alphaType;
        this.R = r ?? new();
        this.G = g ?? new();
        this.B = b ?? new();
        this.A = a ?? new();
        this.X1 = x1 ?? new();
        this.X2 = x2 ?? new();
        this.Bpp = new[] {
            this.R.Bits + this.R.Shift, this.G.Bits + this.G.Shift, this.B.Bits + this.B.Shift,
            this.A.Bits + this.A.Shift, this.X1.Bits + this.X1.Shift, this.X2.Bits + this.X2.Shift,
        }.Max();
    }

    public ChannelDefinition R { get; }
    public ChannelDefinition G { get; }
    public ChannelDefinition B { get; }
    public ChannelDefinition A { get; }
    public ChannelDefinition X1 { get; }
    public ChannelDefinition X2 { get; }

    public AlphaType Alpha { get; }
    public int Bpp { get; }

    public void ToB8G8R8A8(
        Span<byte> target,
        int targetStride,
        ReadOnlySpan<byte> source,
        int sourceStride,
        int width,
        int height)
    {
        for (var y = 0; y < height; y++) {
            var inOffset = y * sourceStride;
            var inOffsetTo = inOffset + (width * this.Bpp + 7) / 8;
            var outOffset = y * targetStride;

            var bits = 0ul;
            var availBits = 0;
            for (var x = 0; x < width && inOffset < inOffsetTo; inOffset++) {
                bits |= (ulong) source[inOffset] << availBits;
                availBits += 8;
                for (; availBits >= this.Bpp && x < width; x++, availBits -= this.Bpp) {
                    target[outOffset++] = (byte) this.B.DecodeValueAsUnorm(bits, 8);
                    target[outOffset++] = (byte) this.G.DecodeValueAsUnorm(bits, 8);
                    target[outOffset++] = (byte) this.R.DecodeValueAsUnorm(bits, 8);
                    target[outOffset++] = (byte) (this.A.Bits == 0 ? 255 : this.A.DecodeValueAsUnorm(bits, 8));
                    bits = this.Bpp >= 64 ? 0 : bits >> this.Bpp;
                }
            }
        }
    }

    // If colors are wrong, then it means that I got orders wrong, and it needs to be modified.

    public static RgbaPixFmt NewR(
        int rbits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) => new(
        alphaType: alphaType,
        r: new(valueType, 0, rbits),
        x1: new(ValueType.Typeless, rbits, xbits1),
        x2: new(ValueType.Typeless, rbits + xbits1, xbits2));

    public static RgbaPixFmt NewA(
        int abits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) => new(
        alphaType: alphaType,
        a: new(valueType, 0, abits),
        x1: new(ValueType.Typeless, abits, xbits1),
        x2: new(ValueType.Typeless, abits + xbits1, xbits2));

    public static RgbaPixFmt NewRg(
        int rbits,
        int gbits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) => new(
        alphaType: alphaType,
        r: new(valueType, 0, rbits),
        g: new(valueType, rbits, gbits),
        x1: new(ValueType.Typeless, rbits + gbits, xbits1),
        x2: new(ValueType.Typeless, rbits + gbits + xbits1, xbits2));

    public static RgbaPixFmt NewRgb(
        int rbits,
        int gbits,
        int bbits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) => new(
        alphaType: alphaType,
        r: new(valueType, 0, rbits),
        g: new(valueType, rbits, gbits),
        b: new(valueType, rbits + gbits, bbits),
        x1: new(ValueType.Typeless, rbits + gbits + bbits, xbits1),
        x2: new(ValueType.Typeless, rbits + gbits + bbits + xbits1, xbits2));

    public static RgbaPixFmt NewRgba(
        int rbits,
        int gbits,
        int bbits,
        int abits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) =>
        new(
            alphaType: alphaType,
            r: new(valueType, 0, rbits),
            g: new(valueType, rbits, gbits),
            b: new(valueType, rbits + gbits, bbits),
            a: new(valueType, rbits + gbits + bbits, abits),
            x1: new(ValueType.Typeless, rbits + gbits + bbits + abits, xbits1),
            x2: new(ValueType.Typeless, rbits + gbits + bbits + abits + xbits1, xbits2));

    public static RgbaPixFmt NewBgr(
        int rbits,
        int gbits,
        int bbits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) => new(
        alphaType: alphaType,
        b: new(valueType, 0, bbits),
        g: new(valueType, bbits, gbits),
        r: new(valueType, bbits + gbits, rbits),
        x1: new(ValueType.Typeless, bbits + gbits + rbits, xbits1),
        x2: new(ValueType.Typeless, bbits + gbits + rbits + xbits1, xbits2));

    public static RgbaPixFmt NewBgra(
        int rbits,
        int gbits,
        int bbits,
        int abits,
        int xbits1 = 0,
        int xbits2 = 0,
        ValueType valueType = ValueType.Unorm,
        AlphaType alphaType = AlphaType.Straight) =>
        new(
            alphaType: alphaType,
            b: new(valueType, 0, bbits),
            g: new(valueType, bbits, gbits),
            r: new(valueType, bbits + gbits, rbits),
            a: new(valueType, bbits + gbits + rbits, abits, (1u << abits) - 1u),
            x1: new(ValueType.Typeless, bbits + gbits + rbits + abits, xbits1),
            x2: new(ValueType.Typeless, bbits + gbits + rbits + abits + xbits1, xbits2));

    public bool Equals(RgbaPixFmt? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.R.Equals(other.R) && this.G.Equals(other.G) && this.B.Equals(other.B) && this.A.Equals(other.A) &&
            this.X1.Equals(other.X1) && this.X2.Equals(other.X2) && this.Alpha == other.Alpha;
    }

    public override bool Equals(object? obj) => this.Equals(obj as RgbaPixFmt);

    public override int GetHashCode() => HashCode.Combine(
        this.R,
        this.G,
        this.B,
        this.A,
        this.X1,
        this.X2,
        (int) this.Alpha);
}
