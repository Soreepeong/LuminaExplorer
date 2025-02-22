using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

public sealed class TextureSqpackFileStream : BaseSqpackFileStream {
    private readonly OffsetManager _offsetManager;

    private LuminaBinaryReader? _reader;

    private int _bufferLodIndex = -1, _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public TextureSqpackFileStream(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info)
        : base(platformId, info.RawFileSize) =>
        this._offsetManager = new(datPath, platformId, baseOffset, info);

    public TextureSqpackFileStream(TextureSqpackFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) =>
        this._offsetManager = cloneFrom._offsetManager;

    ~TextureSqpackFileStream()
    {
        this.Dispose(false);
    }

    public override async Task<int>
        ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (count == 0)
            return 0;

        var totalRead = 0;

        // 0. Header
        if (this.PositionUint < this._offsetManager.HeaderBytes.Length) {
            var consumed = (int) this.PositionUint;
            var remaining = this._offsetManager.HeaderBytes.Length - consumed;
            var available = Math.Min(count, remaining);
            Array.Copy(this._offsetManager.HeaderBytes, consumed, buffer, consumed, available);
            offset += available;
            count -= available;
            this.PositionUint += (uint) available;
            totalRead += available;
            if (count == 0)
                return totalRead;
        }

        // 1. Drain previous read
        if (this._blockBuffer is not null) {
            var blockGroup = this._offsetManager.Lods[this._bufferLodIndex];
            if (blockGroup.RequestOffsets[this._bufferBlockIndex] <= this.PositionUint &&
                this.PositionUint < blockGroup.RequestOffsets[this._bufferBlockIndex + 1]) {
                var bufferConsumed = (int) (this.Position - blockGroup.RequestOffsets[this._bufferBlockIndex]);
                var bufferRemaining = (int) (blockGroup.RequestOffsets[this._bufferBlockIndex + 1] - this.Position);
                if (bufferConsumed < blockGroup.Sizes[this._bufferBlockIndex] && bufferRemaining > 0) {
                    var available = Math.Min(bufferRemaining, count);
                    Array.Copy(this._blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    this.Position += available;
                    totalRead += available;
                    if (available == bufferRemaining) {
                        this._bufferBlockIndex = this._bufferLodIndex = -1;
                        this._bufferValidSize = 0;
                        ArrayPool<byte>.Shared.Return(ref this._blockBuffer);
                    }

                    if (count == 0)
                        return totalRead;
                }
            }
        }

        // 2. New blocks!
        byte[]? readBuffer = null;
        try {
            // There will never be more than 16 mipmaps (width and height are u16 values,) so just count it.
            for (var i = 0; i < this._offsetManager.NumLods && count > 0; i++) {
                cancellationToken.ThrowIfCancellationRequested();

                var lod = this._offsetManager.Lods[i];

                if (this.PositionUint >= lod.RequestOffsets[0] + lod.Summary.DecompressedSize)
                    continue;

                // There can be many subblocks on the other hand.
                var j = Array.BinarySearch(lod.RequestOffsets, 0, lod.RequestOffsets.Length - 1, this.PositionUint);
                if (j < 0)
                    j = ~j - 1;

                if (j == -1) {
                    totalRead += this.ReadImplPadTo(buffer, ref offset, ref count, lod.RequestOffsets[0]);
                    if (count == 0)
                        break;
                    j = 0;
                }

                for (; j < lod.Summary.BlockCount; j++) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (lod.RequestOffsets[j + 1] <= this.PositionUint && lod.RequestOffsets[j] != uint.MaxValue)
                        continue;

                    readBuffer = ArrayPool<byte>.Shared.RentAsNecessary(readBuffer, 16384);
                    await (this._reader ??= this._offsetManager.CreateNewReader())
                        .WithSeek(this._offsetManager.BaseOffset + lod.Offsets[j])
                        .BaseStream.ReadExactlyAsync(new(readBuffer, 0, lod.Sizes[j]), cancellationToken);

                    DatBlockHeader dbh;
                    unsafe {
                        fixed (void* p = readBuffer)
                            dbh = *(DatBlockHeader*) p;
                    }

                    lod.DecompressedSizes[j] = checked((ushort) dbh.DecompressedSize);
                    lod.RequestOffsets[j + 1] = lod.RequestOffsets[j] + lod.DecompressedSizes[j];

                    if (lod.RequestOffsets[j + 1] <= this.PositionUint)
                        continue;

                    var bufferConsumed = this.PositionUint - lod.RequestOffsets[j];
                    var bufferRemaining = lod.RequestOffsets[j + 1] - this.PositionUint;

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
                        Array.Copy(readBuffer, 0, this._blockBuffer, 0, dbh.DecompressedSize);
                    }

                    this._bufferLodIndex = i;
                    this._bufferBlockIndex = j;
                    this._bufferValidSize = dbh.DecompressedSize;

                    if (bufferConsumed < this._bufferValidSize) {
                        var available = Math.Min((int) bufferRemaining, count);
                        Array.Copy(this._blockBuffer, bufferConsumed, buffer, offset, available);
                        offset += available;
                        count -= available;
                        this.PositionUint += (uint) available;
                        totalRead += available;
                        if (available == bufferRemaining) {
                            this._bufferLodIndex = this._bufferBlockIndex = -1;
                            this._bufferValidSize = 0;
                        }

                        if (count == 0)
                            break;
                    }
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

    public override BaseSqpackFileStream Clone(bool keepOpen) => new TextureSqpackFileStream(this);

    protected override void Dispose(bool disposing)
    {
        this.CloseButOpenAgainWhenNecessary();
        base.Dispose(disposing);
    }

    public TexFile.TexHeader TexHeader => this._offsetManager.Header;

    public override void CloseButOpenAgainWhenNecessary()
    {
        SafeDispose.One(ref this._reader);
    }

    private class OffsetManager : BaseOffsetManager {
        public readonly int NumLods;
        public readonly LodBlock[] Lods;
        public readonly TexFile.TexHeader Header;
        public readonly byte[] HeaderBytes;

        public unsafe OffsetManager(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info)
            : base(datPath, platformId, baseOffset)
        {
            this.NumLods = (int) info.NumberOfBlocks;

            using var reader = this.CreateNewReader();
            var locators = reader
                .WithSeek(this.BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<LodBlockStruct>(this.NumLods);

            var texHeaderLength = locators[0].CompressedOffset;

            this.Lods = new LodBlock[this.NumLods];
            for (var i = 0; i < this.NumLods; i++) {
                var baseRequestOffset = i == 0 ? texHeaderLength : this.Lods[i - 1].RequestOffsets[^1];
                var blockSizes = reader.ReadStructuresAsArray<ushort>((int) locators[i].BlockCount);

                this.Lods[i] = new(locators[i], blockSizes, baseRequestOffset, info.Size);
            }

            this.HeaderBytes = reader.WithSeek(this.BaseOffset + info.Size).ReadBytes((int) texHeaderLength);
            fixed (void* p = this.HeaderBytes) this.Header = *(TexFile.TexHeader*) p;
        }
    }

#pragma warning disable CS0649
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [StructLayout(LayoutKind.Sequential)]
    private struct LodBlockStruct {
        public uint CompressedOffset;
        public uint CompressedSize;
        public uint DecompressedSize;
        public uint BlockOffset;
        public uint BlockCount;
    }

    private class LodBlock {
        public readonly LodBlockStruct Summary;
        public readonly uint[] RequestOffsets;
        public readonly uint[] Offsets;
        public readonly ushort[] Sizes;
        public readonly ushort[] DecompressedSizes;

        public LodBlock(LodBlockStruct locator, ushort[] blockSizes, uint baseRequestOffset, uint headerSize)
        {
            this.Summary = locator;
            this.Sizes = blockSizes;

            this.RequestOffsets = new uint[locator.BlockCount + 1];
            Array.Fill(this.RequestOffsets, uint.MaxValue);
            this.RequestOffsets[0] = baseRequestOffset;
            this.RequestOffsets[^1] = baseRequestOffset + this.Summary.DecompressedSize;

            this.Offsets = new uint[locator.BlockCount];
            this.Offsets[0] = headerSize + locator.CompressedOffset;
            for (var i = 1; i < locator.BlockCount; i++) this.Offsets[i] = this.Offsets[i - 1] + this.Sizes[i - 1];

            this.DecompressedSizes = new ushort[locator.BlockCount];
            Array.Fill(this.DecompressedSizes, ushort.MaxValue);
        }
    }
#pragma warning restore CS0649
}
