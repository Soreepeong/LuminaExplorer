using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Structs;
using Lumina.Extensions;
using LuminaExplorer.Util;

namespace LuminaExplorer.LazySqPackTree.VirtualFileStream;

public class StandardVirtualFileStream : BaseVirtualFileStream {
    private readonly OffsetManager _offsetManager;

    private int _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private readonly byte[] _readBuffer = new byte[16384];
    private readonly byte[] _blockBuffer = new byte[16000];

    public StandardVirtualFileStream(LuminaBinaryReader reader, long baseOffset, uint headerSize, uint numBlocks, uint length,
        uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(length, reservedSpaceUnits, occupiedSpaceUnits) {
        _offsetManager = new(reader, baseOffset, headerSize, numBlocks, length);
    }

    public StandardVirtualFileStream(StandardVirtualFileStream cloneFrom) 
        : base((uint)cloneFrom.Length, cloneFrom.ReservedSpaceUnits, cloneFrom.OccupiedSpaceUnits) {
        _offsetManager = cloneFrom._offsetManager;
    }

    public override unsafe int Read(byte[] buffer, int offset, int count) {
        if (count == 0)
            return 0;

        var totalRead = 0;

        // 1. Drain previous read
        if (0 <= _bufferBlockIndex && _bufferBlockIndex < _offsetManager.NumBlocks) {
            if (_offsetManager.RequestOffsets[_bufferBlockIndex] <= PositionUint &&
                PositionUint < _offsetManager.RequestOffsets[_bufferBlockIndex + 1]) {
                var bufferConsumed = PositionUint - _offsetManager.RequestOffsets[_bufferBlockIndex];
                var bufferRemaining = _offsetManager.RequestOffsets[_bufferBlockIndex + 1] - PositionUint;
                if (bufferConsumed < _bufferValidSize && bufferRemaining > 0) {
                    var available = Math.Min((int) bufferRemaining, count);
                    Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    PositionUint += (uint) available;
                    totalRead += available;
                    if (available == bufferRemaining) {
                        _bufferBlockIndex = -1;
                        _bufferValidSize = 0;
                    }

                    if (count == 0)
                        return totalRead;
                }
            }
        }

        // 2. New blocks!
        var i = Array.BinarySearch(_offsetManager.RequestOffsets, PositionUint);
        if (i < 0)
            i = ~i - 1;

        fixed (void* p = _readBuffer) {
            var dbh = (DatBlockHeader*) p;
            var dbhSize = sizeof(DatBlockHeader);
                
            for (; i < _offsetManager.NumBlocks; i++) {
                if (_offsetManager.RequestOffsets[i + 1] <= PositionUint)
                    continue;

                var bufferConsumed = PositionUint - _offsetManager.RequestOffsets[i];
                var bufferRemaining = _offsetManager.RequestOffsets[i + 1] - PositionUint;

                lock (_offsetManager.Reader) {
                    _offsetManager.Reader
                        .WithSeek(_offsetManager.BaseOffset + _offsetManager.BlockOffsets[i])
                        .ReadFully(new(_readBuffer, 0, _offsetManager.BlockSizes[i]));
                }

                if (dbh->IsCompressed) {
                    using var zlibStream = new DeflateStream(
                        new MemoryStream(_readBuffer, dbhSize, (int)dbh->CompressedSize),
                        CompressionMode.Decompress);
                    zlibStream.ReadFully(new(_blockBuffer, 0, (int)dbh->DecompressedSize));
                } else {
                    Array.Copy(_readBuffer, 0, _blockBuffer, 0, dbh->DecompressedSize);
                }
                    
                _bufferBlockIndex = i;
                _bufferValidSize = dbh->DecompressedSize;

                if (bufferConsumed < _bufferValidSize) {
                    var available = Math.Min((int)bufferRemaining, count);
                    Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    PositionUint += (uint)available;
                    totalRead += available;
                    if (available == bufferRemaining) {
                        _bufferBlockIndex = -1;
                        _bufferValidSize = 0;
                    }
                        
                    if (count == 0)
                        break;
                }
            }
        }

        // 3. Pad.
        totalRead += ReadImplPadTo(buffer, ref offset, ref count, (uint)Length);

        return totalRead;
    }

    public override FileType Type => FileType.Standard;

    public override object Clone() => new StandardVirtualFileStream(this);

    private class OffsetManager {
        public readonly LuminaBinaryReader Reader;
        public readonly long BaseOffset;
        public readonly int NumBlocks;
        public readonly uint[] RequestOffsets;
        public readonly uint[] BlockOffsets;
        public readonly ushort[] BlockSizes;

        public OffsetManager(LuminaBinaryReader reader, long baseOffset, uint headerSize, uint numBlocks, uint length) {
            Reader = reader;
            BaseOffset = baseOffset;
            NumBlocks = (int) numBlocks;
            RequestOffsets = new uint[numBlocks + 1];
            RequestOffsets[^1] = length;
            BlockOffsets = new uint[numBlocks];
            BlockSizes = new ushort[numBlocks];
            
            var blockInfos = reader
                .WithSeek(BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<DatStdFileBlockInfos>(NumBlocks);

            for (var i = 0; i < NumBlocks; i++) {
                RequestOffsets[i] = i == 0 ? 0 : RequestOffsets[i - 1] + blockInfos[i - 1].UncompressedSize;
                BlockSizes[i] = blockInfos[i].CompressedSize;
                BlockOffsets[i] = headerSize + blockInfos[i].Offset;
            }
        }
    }
}