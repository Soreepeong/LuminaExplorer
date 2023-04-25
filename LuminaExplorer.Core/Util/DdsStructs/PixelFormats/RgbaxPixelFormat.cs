using System;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct RgbaxPixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition R;
    public readonly ColorChannelDefinition G;
    public readonly ColorChannelDefinition B;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X1;
    public readonly ColorChannelDefinition X2;

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