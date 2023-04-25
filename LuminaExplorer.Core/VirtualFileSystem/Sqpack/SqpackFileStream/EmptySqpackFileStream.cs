using Lumina.Data.Structs;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

public class EmptySqpackFileStream : BaseSqpackFileStream {
    public EmptySqpackFileStream(PlatformId platformId)
        : base(platformId, 0) { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    // This stream is immutable (length=0).
    public override BaseSqpackFileStream Clone(bool keepOpen) => this;
}
