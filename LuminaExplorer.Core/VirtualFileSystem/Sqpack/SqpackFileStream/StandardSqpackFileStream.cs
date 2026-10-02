using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

public sealed class StandardSqpackFileStream : BaseSqpackFileStream {
    private readonly OffsetManager _offsetManager;

    private LuminaBinaryReader? _reader;

    private int _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public StandardSqpackFileStream(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info)
        : base(platformId, info.RawFileSize) =>
        this._offsetManager = new(datPath, platformId, baseOffset, info);

    public StandardSqpackFileStream(StandardSqpackFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) =>
        this._offsetManager = cloneFrom._offsetManager;

    ~StandardSqpackFileStream()
    {
        this.Dispose(false);
    }

    public override async Task<int>
        ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (this._offsetManager is null)
            throw new ObjectDisposedException(nameof(ModelSqpackFileStream));

        if (count == 0)
            return 0;

        var totalRead = 0;

        // 1. Drain previous read
        if (this._blockBuffer is not null) {
            if (this._offsetManager.RequestOffsets[this._bufferBlockIndex] <= this.PositionUint &&
                this.PositionUint < this._offsetManager.RequestOffsets[this._bufferBlockIndex + 1]) {
                var bufferConsumed = this.PositionUint - this._offsetManager.RequestOffsets[this._bufferBlockIndex];
                var bufferRemaining =
                    this._offsetManager.RequestOffsets[this._bufferBlockIndex + 1] - this.PositionUint;
                if (bufferConsumed < this._bufferValidSize && bufferRemaining > 0) {
                    var available = Math.Min((int) bufferRemaining, count);
                    Array.Copy(this._blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    this.PositionUint += (uint) available;
                    totalRead += available;
                    if (available == bufferRemaining) {
                        this._bufferBlockIndex = -1;
                        this._bufferValidSize = 0;
                        ArrayPool<byte>.Shared.Return(ref this._blockBuffer);
                    }

                    if (count == 0)
                        return totalRead;
                }
            }
        }

        // 2. New blocks!
        var i = Array.BinarySearch(this._offsetManager.RequestOffsets, this.PositionUint);
        if (i < 0)
            i = ~i - 1;

        byte[]? readBuffer = null;
        try {
            for (; i < this._offsetManager.NumBlocks; i++) {
                cancellationToken.ThrowIfCancellationRequested();

                if (this._offsetManager.RequestOffsets[i + 1] <= this.PositionUint)
                    continue;

                var bufferConsumed = this.PositionUint - this._offsetManager.RequestOffsets[i];
                var bufferRemaining = this._offsetManager.RequestOffsets[i + 1] - this.PositionUint;

                readBuffer = ArrayPool<byte>.Shared.RentAsNecessary(readBuffer, 16384);
                await (this._reader ??= this._offsetManager.CreateNewReader())
                    .WithSeek(this._offsetManager.BaseOffset + this._offsetManager.BlockOffsets[i])
                    .BaseStream.ReadExactlyAsync(
                        new(readBuffer, 0, this._offsetManager.BlockSizes[i]),
                        cancellationToken).ConfigureAwait(false);

                DatBlockHeader dbh;
                unsafe {
                    fixed (void* p = readBuffer)
                        dbh = *(DatBlockHeader*) p;
                }

                cancellationToken.ThrowIfCancellationRequested();

                this._blockBuffer = ArrayPool<byte>.Shared.RentAsNecessary(
                    this._blockBuffer,
                    (int) dbh.DecompressedSize);
                if (dbh.IsCompressed) {
                    unsafe {
                        fixed (byte* b1 = &readBuffer[Unsafe.SizeOf<DatBlockHeader>()]) {
                            using var s1 = new DeflateStream(
                                new UnmanagedMemoryStream(b1, dbh.CompressedSize),
                                CompressionMode.Decompress);
                            s1.ReadExactly(new(this._blockBuffer, 0, (int) dbh.DecompressedSize));
                        }
                    }
                } else {
                    // Uncompressed data follows the block header, as compressed data does.
                    Array.Copy(readBuffer, Unsafe.SizeOf<DatBlockHeader>(), this._blockBuffer, 0, dbh.DecompressedSize);
                }

                this._bufferBlockIndex = i;
                this._bufferValidSize = dbh.DecompressedSize;

                if (bufferConsumed < this._bufferValidSize) {
                    var available = Math.Min((int) bufferRemaining, count);
                    Array.Copy(this._blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    this.PositionUint += (uint) available;
                    totalRead += available;
                    if (available == bufferRemaining) {
                        this._bufferBlockIndex = -1;
                        this._bufferValidSize = 0;
                    }

                    if (count == 0)
                        break;
                }
            }
        } finally {
            ArrayPool<byte>.Shared.Return(ref readBuffer);
            if (this._bufferValidSize == 0)
                ArrayPool<byte>.Shared.Return(ref this._blockBuffer);
        }

        // 3. Pad.
        totalRead += this.ReadImplPadTo(buffer, ref offset, ref count, (uint) this.Length);

        return totalRead;
    }

    public override BaseSqpackFileStream Clone(bool keepOpen) => new StandardSqpackFileStream(this);

    protected override void Dispose(bool disposing)
    {
        this.CloseButOpenAgainWhenNecessary();
        base.Dispose(disposing);
    }

    public override void CloseButOpenAgainWhenNecessary()
    {
        SafeDispose.One(ref this._reader);
    }

    private class OffsetManager : BaseOffsetManager {
        public readonly int NumBlocks;
        public readonly uint[] RequestOffsets;
        public readonly uint[] BlockOffsets;
        public readonly ushort[] BlockSizes;

        public OffsetManager(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info)
            : base(datPath, platformId, baseOffset)
        {
            this.NumBlocks = (int) info.NumberOfBlocks;
            this.RequestOffsets = new uint[this.NumBlocks + 1];
            this.RequestOffsets[^1] = info.RawFileSize;
            this.BlockOffsets = new uint[this.NumBlocks];
            this.BlockSizes = new ushort[this.NumBlocks];

            using var reader = this.CreateNewReader();
            var blockInfos = reader
                .WithSeek(this.BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<DatStdFileBlockInfos>(this.NumBlocks);

            for (var i = 0; i < this.NumBlocks; i++) {
                this.RequestOffsets[i] = i == 0 ? 0 : this.RequestOffsets[i - 1] + blockInfos[i - 1].UncompressedSize;
                this.BlockSizes[i] = blockInfos[i].CompressedSize;
                this.BlockOffsets[i] = info.Size + blockInfos[i].Offset;
            }
        }
    }
}
