using System;
using System.Collections.Generic;
using System.Drawing;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct UnknownPixelFormat : IPixelFormat {
    public static readonly UnknownPixelFormat Instance = new();
    
    public DxgiFormat DxgiFormat => DxgiFormat.Unknown;
    public DdsFourCc FourCc => DdsFourCc.Unknown;
    
    public IEnumerator<Color> ToColors(ReadOnlySpan<byte> data, int width, int height, int stride) => throw new NotSupportedException();
}