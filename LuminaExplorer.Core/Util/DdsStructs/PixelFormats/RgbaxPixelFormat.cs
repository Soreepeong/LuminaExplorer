using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct RgbaxPixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition R;
    public readonly ColorChannelDefinition G;
    public readonly ColorChannelDefinition B;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X1;
    public readonly ColorChannelDefinition X2;

    public RgbaxPixelFormat(
        ColorChannelDefinition? r = null,
        ColorChannelDefinition? g = null,
        ColorChannelDefinition? b = null,
        AlphaChannelDefinition? a = null,
        ColorChannelDefinition? x1 = null,
        ColorChannelDefinition? x2 = null) {
        R = r ?? new();
        G = g ?? new();
        B = b ?? new();
        A = a ?? new();
        X1 = x1 ?? new();
        X2 = x2 ?? new();
        Bpp = new[] {
            R.Bits + R.Shift,
            G.Bits + G.Shift,
            B.Bits + B.Shift,
            A.Bits + A.Shift,
            X1.Bits + X1.Shift,
            X2.Bits + X2.Shift,
        }.Max();
    }

    public int Bpp { get; }

    public void ToB8G8R8A8(Span<byte> target, int targetStride, ReadOnlySpan<byte> source, int sourceStride, int width,
        int height) {
        var bits = 0ul;
        var availBits = 0;
        
        for (var y = 0; y < height; y++) {
            var inOffset = y * sourceStride;
            var inOffsetTo = inOffset + (width * Bpp + 7) / 8;
            var outOffset = y * targetStride;
            
            for (var x = 0; x < width && inOffset < inOffsetTo; inOffset++) {
                bits = (bits << 8) | source[inOffset];
                availBits += 8;
                for (; availBits >= Bpp && x < width; x++, availBits -= Bpp) {
                    target[outOffset++] = (byte)(A.Bits == 0 ? 255 : A.DecodeValueAsInt(bits, 8));
                    target[outOffset++] = (byte)R.DecodeValueAsInt(bits, 8);
                    target[outOffset++] = (byte)G.DecodeValueAsInt(bits, 8);
                    target[outOffset++] = (byte)B.DecodeValueAsInt(bits, 8);
                }
            }
        }
    }

    public static RgbaxPixelFormat CreateR(ValueType valueType, int rbits, int xbits1 = 0, int xbits2 = 0) => new(
        r: new(valueType, xbits1 + xbits2, rbits),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateA(ValueType valueType, int abits, int xbits1 = 0, int xbits2 = 0) => new(
        a: new(valueType, AlphaType.Straight, xbits1 + xbits2, abits, (1u << abits) - 1u),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateRg(ValueType valueType, int rbits, int gbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        r: new(valueType, gbits + xbits1 + xbits2, rbits),
        g: new(valueType, xbits1 + xbits2, gbits),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateRgb(ValueType valueType, int rbits, int gbits, int bbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        r: new(valueType, gbits + bbits, rbits),
        g: new(valueType, bbits, gbits),
        b: new(valueType, 0, bbits),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateRgba(ValueType valueType, int rbits, int gbits, int bbits, int abits,
        int xbits1 = 0, int xbits2 = 0) =>
        new(
            r: new(valueType, gbits + bbits + abits + xbits1 + xbits2, rbits),
            g: new(valueType, bbits + abits + xbits1 + xbits2, gbits),
            b: new(valueType, abits + xbits1 + xbits2, bbits),
            a: new(valueType, AlphaType.Straight, xbits1 + xbits2, abits),
            x1: new(ValueType.Typeless, xbits2, xbits1),
            x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateBgr(ValueType valueType, int rbits, int gbits, int bbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        b: new(valueType, gbits + rbits + xbits1 + xbits2, bbits),
        g: new(valueType, rbits + xbits1 + xbits2, gbits),
        r: new(valueType, xbits1 + xbits2, rbits),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateBgra(ValueType valueType, int rbits, int gbits, int bbits, int abits,
        int xbits1 = 0, int xbits2 = 0) =>
        new(
            b: new(valueType, gbits + rbits + abits + xbits1 + xbits2, bbits),
            g: new(valueType, rbits + abits + xbits1 + xbits2, gbits),
            r: new(valueType, abits + xbits1 + xbits2, rbits),
            a: new(valueType, AlphaType.Straight, xbits1 + xbits2, abits, (1u << abits) - 1u),
            x1: new(ValueType.Typeless, xbits2, xbits1),
            x2: new(ValueType.Typeless, 0, xbits2));
    
    // If colors are wrong, then it means that I got orders wrong, and it needs to be the below.
    /*
    public static RgbaxPixelFormat CreateR(ValueType valueType, int rbits, int xbits1 = 0, int xbits2 = 0) => new(
        r: new(valueType, 0, rbits),
        x1: new(ValueType.Typeless, r, xbits1),
        x2: new(ValueType.Typeless, r + xbits1, xbits2));

    public static RgbaxPixelFormat CreateA(ValueType valueType, int abits, int xbits1 = 0, int xbits2 = 0) => new(
        a: new(valueType, AlphaType.Straight, 0, abits, (1u << abits) - 1u),
        x1: new(ValueType.Typeless, rbits, xbits1),
        x2: new(ValueType.Typeless, rbits + xbits1, xbits2));

    public static RgbaxPixelFormat CreateRg(ValueType valueType, int rbits, int gbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        r: new(valueType, 0, rbits),
        g: new(valueType, rbits, gbits),
        x1: new(ValueType.Typeless, rbits + gbits, xbits1),
        x2: new(ValueType.Typeless, ribts + gbits + xbits1, xbits2));

    public static RgbaxPixelFormat CreateRgb(ValueType valueType, int rbits, int gbits, int bbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        r: new(valueType, 0, rbits),
        g: new(valueType, rbits, gbits),
        b: new(valueType, rbits + gbits, bbits),
        x1: new(ValueType.Typeless, xbits2, xbits1),
        x2: new(ValueType.Typeless, 0, xbits2));

    public static RgbaxPixelFormat CreateRgba(ValueType valueType, int rbits, int gbits, int bbits, int abits,
        int xbits1 = 0, int xbits2 = 0) =>
        new(
            r: new(valueType, 0, rbits),
            g: new(valueType, rbits, gbits),
            b: new(valueType, rbits + gbits, bbits),
            a: new(valueType, AlphaType.Straight, rbits + gbits + bbits, abits),
            x1: new(ValueType.Typeless, rbits + gbits + bbits + abits, xbits1),
            x2: new(ValueType.Typeless, rbits + gbits + bbits + abits + xbits1, xbits2));

    public static RgbaxPixelFormat CreateBgr(ValueType valueType, int rbits, int gbits, int bbits, int xbits1 = 0,
        int xbits2 = 0) => new(
        b: new(valueType, 0, bbits),
        g: new(valueType, bbits, gbits),
        r: new(valueType, bbits + gbits, rbits),
        x1: new(ValueType.Typeless, bbits + gbits + rbits, xbits1),
        x2: new(ValueType.Typeless, bbits + gbits + rbits + xbits1, xbits2));

    public static RgbaxPixelFormat CreateBgra(ValueType valueType, int rbits, int gbits, int bbits, int abits,
        int xbits1 = 0, int xbits2 = 0) =>
        new(
            b: new(valueType, 0, bbits),
            g: new(valueType, bbits, gbits),
            r: new(valueType, bbits + gbits, rbits),
            a: new(valueType, AlphaType.Straight, bbits + gbits + rbits, abits, (1u << abits) - 1u),
            x1: new(ValueType.Typeless, bbits + gbits + rbits + abits, xbits1),
            x2: new(ValueType.Typeless, bbits + gbits + rbits + abits + xbits1, xbits2));
     */
}
