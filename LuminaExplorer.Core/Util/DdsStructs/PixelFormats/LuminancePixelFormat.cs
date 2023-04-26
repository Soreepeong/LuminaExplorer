using System;
using System.Linq;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct LuminancePixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition L;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X;

    public LuminancePixelFormat(
        ColorChannelDefinition? l = null,
        AlphaChannelDefinition? a = null,
        ColorChannelDefinition? x = null) {
        L = l ?? new();
        A = a ?? new();
        X = x ?? new();

        Bpp = new[] {L.Bits + L.Shift, A.Bits + A.Shift, X.Bits + X.Shift}.Max();
    }

    public int Bpp { get; }

    public void ToB8G8R8A8(Span<byte> target, int targetStride, ReadOnlySpan<byte> source, int sourceStride, int width,
        int height) {
        var bits = 0ul;
        var availBits = 0;
        var outOffset = 0;

        for (var y = 0; y < height; y++) {
            var inOffset = y * sourceStride;
            var inOffsetTo = inOffset + (width * Bpp + 7) / 8;
            
            for (var x = 0; x < width && inOffset < inOffsetTo; inOffset++) {
                bits = (bits << 8) | source[inOffset];
                availBits += 8;
                
                for (; availBits >= Bpp && x < width; x++, availBits -= Bpp) {
                    var l = (byte) L.DecodeValueAsInt(bits, 8);
                    var a = (byte) (A.Bits == 0 ? 255 : A.DecodeValueAsInt(bits, 8));
                    target[outOffset++] = a;
                    target[outOffset++] = l;
                    target[outOffset++] = l;
                    target[outOffset++] = l;
                }
            }
        }
    }
}
