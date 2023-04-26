using System;
using System.Collections.Generic;
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
    public readonly int Bpp;

    public RgbaxPixelFormat(
        ColorChannelDefinition r = new(),
        ColorChannelDefinition g = new(),
        ColorChannelDefinition b = new(),
        AlphaChannelDefinition a = new(),
        ColorChannelDefinition x1 = new(),
        ColorChannelDefinition x2 = new()) {
        R = r;
        G = g;
        B = b;
        A = a;
        X1 = x1;
        X2 = x2;
        Bpp = new[] {
            r.Bits + r.Shift,
            g.Bits + g.Shift,
            b.Bits + b.Shift,
            a.Bits + a.Shift,
            x1.Bits + x1.Shift,
            x2.Bits + x2.Shift,
        }.Max();
    }

    public IEnumerator<Color> ToColors(ReadOnlySpan<byte> data, int width, int height, int stride) {
        var bits = 0ul;
        var availBits = 0;
        for (var y = 0; y < height; y++) {
            var offset = y * stride;
            var offsetTo = offset + (width * Bpp + 7) / 8;
            for (; offset < offsetTo; offset++) {
                bits = (bits << 8) | data[offset];
                availBits += 8;
                if (availBits < Bpp)
                    continue;

                availBits -= Bpp;

                var r = (int) ((bits >> R.Shift) & R.Mask);
                var g = (int) ((bits >> G.Shift) & G.Mask);
                var b = (int) ((bits >> B.Shift) & B.Mask);
                var a = (int) ((bits >> A.Shift) & A.Mask);
                yield return Color.FromArgb(a, r, g, b);
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
}
