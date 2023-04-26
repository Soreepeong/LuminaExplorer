using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct LuminancePixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition L;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X;
    public readonly int Bpp;

    public LuminancePixelFormat(
        ColorChannelDefinition l = new(),
        AlphaChannelDefinition a = new(),
        ColorChannelDefinition x = new()) {
        L = l;
        A = a;
        X = x;

        Bpp = new[] {l.Bits + l.Shift, a.Bits + a.Shift, x.Bits + x.Shift}.Max();
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

                var l = (int) ((bits >> L.Shift) & L.Mask);
                var a = (int) ((bits >> A.Shift) & A.Mask);
                yield return Color.FromArgb(a, l, l, l);
            }
        }
    }
}
