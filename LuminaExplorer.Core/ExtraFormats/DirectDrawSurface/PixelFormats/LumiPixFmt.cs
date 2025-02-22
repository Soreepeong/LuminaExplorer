using System;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats;

public class LumiPixFmt : IPixFmt, IEquatable<LumiPixFmt> {
    public LumiPixFmt(
        AlphaType alphaType,
        ChannelDefinition? l = null,
        ChannelDefinition? a = null,
        ChannelDefinition? x = null)
    {
        this.L = l ?? new();
        this.A = a ?? new();
        this.X = x ?? new();
        this.Alpha = alphaType;

        this.Bpp = new[] { this.L.Bits + this.L.Shift, this.A.Bits + this.A.Shift, this.X.Bits + this.X.Shift }.Max();
    }

    public ChannelDefinition L { get; }

    public ChannelDefinition A { get; }

    public ChannelDefinition X { get; }

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
        var bits = 0ul;
        var availBits = 0;
        var outOffset = 0;

        for (var y = 0; y < height; y++) {
            var inOffset = y * sourceStride;
            var inOffsetTo = inOffset + (width * this.Bpp + 7) / 8;

            for (var x = 0; x < width && inOffset < inOffsetTo; inOffset++) {
                bits = (bits << 8) | source[inOffset];
                availBits += 8;

                for (; availBits >= this.Bpp && x < width; x++, availBits -= this.Bpp) {
                    var l = (byte) this.L.DecodeValueAsUnorm(bits, 8);
                    var a = (byte) (this.A.Bits == 0 ? 255 : this.A.DecodeValueAsUnorm(bits, 8));
                    target[outOffset++] = a;
                    target[outOffset++] = l;
                    target[outOffset++] = l;
                    target[outOffset++] = l;
                }
            }
        }
    }

    public bool Equals(LumiPixFmt? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.L.Equals(other.L) && this.A.Equals(other.A) && this.X.Equals(other.X) && this.Alpha == other.Alpha;
    }

    public override bool Equals(object? obj) => this.Equals(obj as LumiPixFmt);

    public override int GetHashCode() => HashCode.Combine(this.L, this.A, this.X, (int) this.Alpha);
}
