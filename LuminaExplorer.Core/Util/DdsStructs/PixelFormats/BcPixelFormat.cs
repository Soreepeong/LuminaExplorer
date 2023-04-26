using System;
using System.Collections.Generic;
using System.Drawing;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct BcPixelFormat : IPixelFormat {
    public readonly ValueType Type;
    public readonly AlphaType Alpha;
    public readonly byte Version;

    public BcPixelFormat(
        ValueType type = ValueType.Unknown,
        AlphaType alpha = AlphaType.Straight,
        byte version = 0) {
        Type = type;
        Alpha = alpha;
        Version = version;
    }

    public IEnumerator<Color> ToColors(ReadOnlySpan<byte> data, int width, int height, int stride) {
        throw new NotImplementedException();
    }
}