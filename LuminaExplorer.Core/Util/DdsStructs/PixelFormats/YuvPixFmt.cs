using System;
using System.Linq;
using LuminaExplorer.Core.Util.DdsStructs.PixelFormats.Channels;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

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
        ChannelDefinition? x = null) {
        Alpha = alphaType;
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

    public AlphaType Alpha { get; }

    public int Bpp { get; }

    public void ToB8G8R8A8(Span<byte> target, int targetStride, ReadOnlySpan<byte> source, int sourceStride, int width,
        int height) {
        throw new NotImplementedException();
    }

    public bool Equals(YuvPixFmt? other) {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Y.Equals(other.Y) && U.Equals(other.U) && V.Equals(other.V) && A.Equals(other.A) && X.Equals(other.X) &&
            Alpha == other.Alpha;
    }

    public override bool Equals(object? obj) => Equals(obj as YuvPixFmt);

    public override int GetHashCode() => HashCode.Combine(Y, U, V, A, X, (int) Alpha);
}
