using System;
using System.Collections.Generic;
using System.Drawing;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct UnknownPixelFormat : IPixelFormat {
    public static readonly UnknownPixelFormat Instance = new();

    public int Bpp => 0;
    public DxgiFormat DxgiFormat => DxgiFormat.Unknown;
    public DdsFourCc FourCc => DdsFourCc.Unknown;
    
    public void ToB8G8R8A8(Span<byte> target, int targetStride, ReadOnlySpan<byte> source, int sourceStride, int width,
        int height) {
        throw new NotImplementedException();
    }
}