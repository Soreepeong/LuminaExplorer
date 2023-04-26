using System;
using System.Collections.Generic;
using System.Drawing;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public interface IPixelFormat {
    DxgiFormat DxgiFormat => PixelFormatResolver.GetDxgiFormat(this);
    DdsFourCc FourCc => PixelFormatResolver.GetFourCc(this);

    IEnumerator<Color> ToColors(ReadOnlySpan<byte> data, int width, int height, int stride);

    // IEnumerator<Vector4> ToColorsF();
}
