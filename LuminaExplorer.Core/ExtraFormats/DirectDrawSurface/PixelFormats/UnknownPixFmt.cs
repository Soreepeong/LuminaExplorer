using System;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;

public class UnknownPixFmt : IPixFmt, IEquatable<UnknownPixFmt> {
    public static readonly UnknownPixFmt Instance = new();

    private UnknownPixFmt()
    { }

    public AlphaType Alpha => AlphaType.None;
    public int Bpp => 0;
    public DXGI_FORMAT DxgiFormat => DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
    public DdsFourCc FourCc => DdsFourCc.Unknown;

    public void ToB8G8R8A8(
        Span<byte> target,
        int targetStride,
        ReadOnlySpan<byte> source,
        int sourceStride,
        int width,
        int height)
    {
        throw new NotImplementedException();
    }

    public override bool Equals(object? obj) => ReferenceEquals(obj, this);

    public bool Equals(UnknownPixFmt? other) => ReferenceEquals(other, this);

    public override int GetHashCode() => 0x4df85ea8;
}
