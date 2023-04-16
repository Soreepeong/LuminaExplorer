using Lumina.Data.Structs;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public class EmptyVirtualFileStream : BaseVirtualFileStream {
    public EmptyVirtualFileStream(PlatformId platformId, uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(platformId, 0, reservedSpaceUnits, occupiedSpaceUnits) { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override FileType Type => FileType.Empty;

    public override object Clone() => new EmptyVirtualFileStream(PlatformId, ReservedSpaceUnits, OccupiedSpaceUnits);
}