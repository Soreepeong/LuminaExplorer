using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct YuvPixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition Y;
    public readonly ColorChannelDefinition U;
    public readonly ColorChannelDefinition V;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X;

    public YuvPixelFormat(
        ColorChannelDefinition? y = null,
        ColorChannelDefinition? u = null,
        ColorChannelDefinition? v = null,
        AlphaChannelDefinition? a = null,
        ColorChannelDefinition? x = null) {
        Y = y ?? new();
        U = u ?? new();
        V = v ?? new();
        A = a ?? new();
        X = x ?? new();
        Bpp = new[] {
            Y.Bits + Y.Shift,
            U.Bits + U.Shift,
            V.Bits + V.Shift,
            A.Bits + A.Shift,
            X.Bits + X.Shift,
        }.Max();
    }

    public int Bpp { get; }
    
    public void ToB8G8R8A8(Span<byte> target, int targetStride, ReadOnlySpan<byte> source, int sourceStride, int width,
        int height) {
        throw new NotImplementedException();
    }
}
