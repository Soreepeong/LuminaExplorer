using System;
using LuminaExplorer.Core.Util.DdsStructs.PixelFormats.Channels;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public interface IPixFmt {
    AlphaType Alpha { get; }
    int Bpp { get; }
    DxgiFormat DxgiFormat => PixFmtResolver.GetDxgiFormat(this);
    DdsFourCc FourCc => PixFmtResolver.GetFourCc(this);
    Guid WicFormat => PixFmtResolver.GetWicPixelFormat(this);

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
