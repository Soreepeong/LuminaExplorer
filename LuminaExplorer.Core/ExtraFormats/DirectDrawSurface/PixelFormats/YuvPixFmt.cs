using System;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;

public class YuvPixFmt : IPixFmt, IEquatable<YuvPixFmt> {
    public readonly ChannelDefinition Y;
    public readonly ChannelDefinition U;
    public readonly ChannelDefinition V;
    public readonly ChannelDefinition A;
    public readonly ChannelDefinition X;

    public YuvPixFmt(
        AlphaType alphaType,
        ChannelDefinition? y = null,
        ChannelDefinition? u = null,
        ChannelDefinition? v = null,
        ChannelDefinition? a = null,
        ChannelDefinition? x = null)
    {
        this.Alpha = alphaType;
        this.Y = y ?? new();
        this.U = u ?? new();
        this.V = v ?? new();
        this.A = a ?? new();
        this.X = x ?? new();
        this.Bpp = new[] {
            this.Y.Bits + this.Y.Shift, this.U.Bits + this.U.Shift, this.V.Bits + this.V.Shift,
            this.A.Bits + this.A.Shift, this.X.Bits + this.X.Shift,
        }.Max();
    }

    public AlphaType Alpha { get; }

    public int Bpp { get; }

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

    public bool Equals(YuvPixFmt? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.Y.Equals(other.Y) && this.U.Equals(other.U) && this.V.Equals(other.V) && this.A.Equals(other.A) &&
            this.X.Equals(other.X) && this.Alpha == other.Alpha;
    }

    public override bool Equals(object? obj) => this.Equals(obj as YuvPixFmt);

    public override int GetHashCode() => HashCode.Combine(this.Y, this.U, this.V, this.A, this.X, (int) this.Alpha);
}
