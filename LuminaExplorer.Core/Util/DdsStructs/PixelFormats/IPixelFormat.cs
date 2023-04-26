using System;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public interface IPixelFormat {
    int Bpp { get; }
    DxgiFormat DxgiFormat => PixelFormatResolver.GetDxgiFormat(this);
    DdsFourCc FourCc => PixelFormatResolver.GetFourCc(this);

    void ToB8G8R8A8(
        Span<byte> target,
        int targetStride,
        ReadOnlySpan<byte> source,
        int sourceStride,
        int width,
        int height);

    void ToB8G8R8A8(
        nint targetAddress,
        int targetSize,
        int targetStride,
        ReadOnlySpan<byte> source,
        int sourceStride,
        int width,
        int height) {
        unsafe {
            ToB8G8R8A8(new((void*) targetAddress, targetSize), targetStride, source, sourceStride, width, height);
        }
    }

    // IEnumerator<Vector4> ToColorsF();
}
