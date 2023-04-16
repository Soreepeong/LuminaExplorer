using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public class StandardVirtualFileStream : BaseVirtualFileStream {
    private readonly OffsetManager _offsetManager;

    private int _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public StandardVirtualFileStream(LuminaBinaryReader reader, long baseOffset, SqPackFileInfo info)
        : base(reader.PlatformId, info.RawFileSize) {
        _offsetManager = new(reader, baseOffset, info);
    }

    public StandardVirtualFileStream(StandardVirtualFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) {
        _offsetManager = cloneFrom._offsetManager;
    }

    public override async Task<int>
        ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        if (count == 0)
            return 0;

        var totalRead = 0;

        // 1. Drain previous read
        if (_blockBuffer is not null) {
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
                        ArrayPool<byte>.Shared.Return(ref _blockBuffer);
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

        byte[]? readBuffer = null;
        try {
            for (; i < _offsetManager.NumBlocks; i++) {
                cancellationToken.ThrowIfCancellationRequested();

                if (_offsetManager.RequestOffsets[i + 1] <= PositionUint)
                    continue;

                var bufferConsumed = PositionUint - _offsetManager.RequestOffsets[i];
                var bufferRemaining = _offsetManager.RequestOffsets[i + 1] - PositionUint;

                await _offsetManager.ReaderLock.WaitAsync(cancellationToken);
                readBuffer = ArrayPool<byte>.Shared.RentAsNecessary(readBuffer, 16384);
                await _offsetManager.Reader
                    .WithSeek(_offsetManager.BaseOffset + _offsetManager.BlockOffsets[i])
                    .BaseStream.ReadExactlyAsync(new(readBuffer, 0, _offsetManager.BlockSizes[i]), cancellationToken);
                _offsetManager.ReaderLock.Release();

                DatBlockHeader dbh;
                unsafe {
                    fixed (void* p = readBuffer)
                        dbh = *(DatBlockHeader*) p;
                }

                cancellationToken.ThrowIfCancellationRequested();

                _blockBuffer = ArrayPool<byte>.Shared.RentAsNecessary(_blockBuffer, (int)dbh.DecompressedSize);
                if (dbh.IsCompressed) {
                    await using var zlibStream = new DeflateStream(
                        new MemoryStream(readBuffer, Unsafe.SizeOf<DatBlockHeader>(), (int) dbh.CompressedSize),
                        CompressionMode.Decompress);
                    zlibStream.ReadExactly(new(_blockBuffer, 0, (int) dbh.DecompressedSize));
                } else {
                    Array.Copy(readBuffer, 0, _blockBuffer, 0, dbh.DecompressedSize);
                }

                _bufferBlockIndex = i;
                _bufferValidSize = dbh.DecompressedSize;

                if (bufferConsumed < _bufferValidSize) {
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
                        break;
                }
            }
        } finally {
            ArrayPool<byte>.Shared.Return(ref readBuffer);
            if (_bufferValidSize == 0)
                ArrayPool<byte>.Shared.Return(ref _blockBuffer);
        }

        // 3. Pad.
        totalRead += ReadImplPadTo(buffer, ref offset, ref count, (uint) Length);

        return totalRead;
    }

    public override object Clone() => new StandardVirtualFileStream(this);

    private class OffsetManager {
        public readonly SemaphoreSlim ReaderLock = new(1, 1);
        public readonly LuminaBinaryReader Reader;
        public readonly long BaseOffset;
        public readonly int NumBlocks;
        public readonly uint[] RequestOffsets;
        public readonly uint[] BlockOffsets;
        public readonly ushort[] BlockSizes;

        public OffsetManager(LuminaBinaryReader reader, long baseOffset, SqPackFileInfo info) {
            Reader = reader;
            BaseOffset = baseOffset;
            NumBlocks = (int) info.NumberOfBlocks;
            RequestOffsets = new uint[NumBlocks + 1];
            RequestOffsets[^1] = info.RawFileSize;
            BlockOffsets = new uint[NumBlocks];
            BlockSizes = new ushort[NumBlocks];

            var blockInfos = reader
                .WithSeek(BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<DatStdFileBlockInfos>(NumBlocks);

            for (var i = 0; i < NumBlocks; i++) {
                RequestOffsets[i] = i == 0 ? 0 : RequestOffsets[i - 1] + blockInfos[i - 1].UncompressedSize;
                BlockSizes[i] = blockInfos[i].CompressedSize;
                BlockOffsets[i] = info.Size + blockInfos[i].Offset;
            }
        }
    }
}
