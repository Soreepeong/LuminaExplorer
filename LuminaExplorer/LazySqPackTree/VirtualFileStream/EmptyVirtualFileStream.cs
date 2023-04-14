using Lumina.Data.Structs;

namespace LuminaExplorer.LazySqPackTree.VirtualFileStream;

public class EmptyVirtualFileStream : BaseVirtualFileStream {
    public EmptyVirtualFileStream(uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(0, reservedSpaceUnits, occupiedSpaceUnits) { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override FileType Type => FileType.Empty;
}