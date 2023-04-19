using System.IO.Compression;

namespace LuminaExplorer.Core.Util;

/// <summary>
/// Placeholder class for when deciding to use raw zlib.
/// </summary>
public sealed class DeflateBytes : IDisposable {
    public void Dispose() { }

    public unsafe void Inflate(ReadOnlySpan<byte> source, Span<byte> target) {
        fixed (byte* b1 = &source.GetPinnableReference()) {
            using var s1 = new DeflateStream(new UnmanagedMemoryStream(b1, source.Length),
                CompressionMode.Decompress);
            s1.ReadExactly(target);
        }
    }
}
