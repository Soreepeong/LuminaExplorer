using Lumina.Data.Structs;

namespace LuminaExplorer.LazySqPackTree.VirtualFileStream;

public abstract class BaseVirtualFileStream : Stream, ICloneable {
    protected uint PositionUint = 0;

    public readonly uint ReservedSpaceUnits;
    public readonly uint OccupiedSpaceUnits;

    protected BaseVirtualFileStream(uint length, uint reservedSpaceUnits, uint occupiedSpaceUnits) {
        Length = length;
        ReservedSpaceUnits = reservedSpaceUnits;
        OccupiedSpaceUnits = occupiedSpaceUnits;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) {
        var newPosition = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
        };
        if (newPosition < 0 || newPosition > Length)
            throw new IOException();
        return PositionUint = (uint) newPosition;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public abstract FileType Type { get; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length { get; }

    public override long Position {
        get => PositionUint;
        set => Seek(value, SeekOrigin.Begin);
    }

    protected int ReadImplPadToEnd(byte[] buffer, int offset, int count) {
        count = (int) Math.Min(Length - Position, count);
        Array.Fill(buffer, (byte) 0, offset, count);
        return count;
    }

    public abstract object Clone();
}
