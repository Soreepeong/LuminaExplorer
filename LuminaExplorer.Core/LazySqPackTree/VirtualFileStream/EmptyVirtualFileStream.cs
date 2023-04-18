using Lumina.Data.Structs;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public class EmptyVirtualFileStream : BaseVirtualFileStream {
    public EmptyVirtualFileStream(PlatformId platformId)
        : base(platformId, 0) { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    // This stream is immutable (length=0).
    public override BaseVirtualFileStream Clone(bool keepOpen) => this;
}
