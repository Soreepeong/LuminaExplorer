using System;
using Lumina.Data.Parsing;

namespace LuminaExplorer.Core.Util;

public static class ColorSetBlender {
    public static byte UInt16To8BitColour(ushort s) =>
        (byte) Math.Clamp(MathF.Floor((float) BitConverter.UInt16BitsToHalf(s) * 256), 0, byte.MaxValue);

    public static byte Blend(byte x, byte y, double scaler) =>
        (byte) Math.Clamp((x * (1 - scaler) + y * scaler) / byte.MaxValue, 0, byte.MaxValue);

    public static Bgra8888 Blend(Bgra8888 x, Bgra8888 y, byte a, double scaler) =>
        new(Blend(x.r, y.r, scaler), Blend(x.g, y.g, scaler), Blend(x.b, y.b, scaler), a);

    public static unsafe Bgra8888 ColorFromSet(ColorSetInfo colorSetInfo, int colorSetIndex) =>
        new(UInt16To8BitColour(colorSetInfo.Data[colorSetIndex + 0]),
            UInt16To8BitColour(colorSetInfo.Data[colorSetIndex + 1]),
            UInt16To8BitColour(colorSetInfo.Data[colorSetIndex + 2]), 255);

    public static Bgra8888 Blend(ColorSetInfo colorSetInfo, int colorSetIndex1, int colorSetIndex2,
        byte alpha, double scaler) => Blend(
        ColorFromSet(colorSetInfo, colorSetIndex1),
        ColorFromSet(colorSetInfo, colorSetIndex2),
        alpha,
        scaler
    );

    public struct Bgra8888 {
        public byte b;
        public byte g;
        public byte r;
        public byte a;

        public Bgra8888() { }

        public Bgra8888(byte r, byte g, byte b, byte a) {
            this.b = b;
            this.g = g;
            this.r = r;
            this.a = a;
        }
    }
}
