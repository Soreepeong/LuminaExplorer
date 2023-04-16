using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using Lumina.Data.Structs;
using Lumina.Extensions;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public class TextureVirtualFileStream : BaseVirtualFileStream {
    private readonly OffsetManager _offsetManager;

    private int _bufferLodIndex = -1, _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private readonly byte[] _readBuffer = new byte[16384];
    private readonly byte[] _blockBuffer = new byte[16000];

    public TextureVirtualFileStream(PlatformId platformId, LuminaBinaryReader reader, long baseOffset, uint headerSize,
        uint numBlocks, uint length, uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(platformId, length, reservedSpaceUnits, occupiedSpaceUnits) {
        _offsetManager = new(reader, baseOffset, headerSize, numBlocks);
    }

    public TextureVirtualFileStream(TextureVirtualFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length, cloneFrom.ReservedSpaceUnits, cloneFrom.OccupiedSpaceUnits) {
        _offsetManager = cloneFrom._offsetManager;
    }

    public override unsafe int Read(byte[] buffer, int offset, int count) {
        if (count == 0)
            return 0;

        var totalRead = 0;

        // 0. Header
        if (PositionUint < _offsetManager.HeaderBytes.Length) {
            var consumed = (int) PositionUint;
            var remaining = _offsetManager.HeaderBytes.Length - consumed;
            var available = Math.Min(count, remaining);
            Array.Copy(_offsetManager.HeaderBytes, consumed, buffer, consumed, available);
            offset += available;
            count -= available;
            PositionUint += (uint) available;
            totalRead += available;
            if (count == 0)
                return totalRead;
        }

        // 1. Drain previous read
        if (0 <= _bufferLodIndex && _bufferLodIndex < _offsetManager.NumLods &&
            0 <= _bufferBlockIndex && _bufferBlockIndex < _offsetManager.Lods[_bufferLodIndex].Summary.BlockCount) {
            var blockGroup = _offsetManager.Lods[_bufferLodIndex];
            if (blockGroup.RequestOffsets[_bufferBlockIndex] <= PositionUint &&
                PositionUint < blockGroup.RequestOffsets[_bufferBlockIndex + 1]) {
                var bufferConsumed = (int) (Position - blockGroup.RequestOffsets[_bufferBlockIndex]);
                var bufferRemaining = (int) (blockGroup.RequestOffsets[_bufferBlockIndex + 1] - Position);
                if (bufferConsumed < blockGroup.Sizes[_bufferBlockIndex] && bufferRemaining > 0) {
                    var available = Math.Min(bufferRemaining, count);
                    Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    Position += available;
                    totalRead += available;
                    if (available == bufferRemaining)
                        _bufferLodIndex = _bufferBlockIndex = -1;
                    if (count == 0)
                        return totalRead;
                }
            }
        }

        // 2. New blocks!
        fixed (void* p = _readBuffer) {
            var dbh = (DatBlockHeader*) p;
            var dbhSize = sizeof(DatBlockHeader);

            // There will never be more than 16 mipmaps (width and height are u16 values,) so just count it.
            for (var i = 0; i < _offsetManager.NumLods && count > 0; i++) {
                var lod = _offsetManager.Lods[i];

                if (PositionUint >= lod.RequestOffsets[0] + lod.Summary.DecompressedSize)
                    continue;

                // There can be many subblocks on the other hand.
                var j = Array.BinarySearch(lod.RequestOffsets, 0, lod.RequestOffsets.Length - 1, PositionUint);
                if (j < 0)
                    j = ~j - 1;

                if (j == -1) {
                    totalRead += ReadImplPadTo(buffer, ref offset, ref count, lod.RequestOffsets[0]);
                    if (count == 0)
                        break;
                    j = 0;
                }

                for (; j < lod.Summary.BlockCount; j++) {
                    if (lod.RequestOffsets[j + 1] <= PositionUint && lod.RequestOffsets[j] != uint.MaxValue)
                        continue;

                    lock (_offsetManager.Reader) {
                        _offsetManager.Reader
                            .WithSeek(_offsetManager.BaseOffset + lod.Offsets[j])
                            .ReadFully(new(_readBuffer, 0, lod.Sizes[j]));
                    }

                    lod.DecompressedSizes[j] = checked((ushort) dbh->DecompressedSize);
                    lod.RequestOffsets[j + 1] = lod.RequestOffsets[j] + lod.DecompressedSizes[j];

                    if (lod.RequestOffsets[j + 1] <= PositionUint)
                        continue;

                    var bufferConsumed = PositionUint - lod.RequestOffsets[j];
                    var bufferRemaining = lod.RequestOffsets[j + 1] - PositionUint;

                    if (dbh->IsCompressed) {
                        using var zlibStream = new DeflateStream(
                            new MemoryStream(_readBuffer, dbhSize, (int) dbh->CompressedSize),
                            CompressionMode.Decompress);
                        zlibStream.ReadFully(new(_blockBuffer, 0, (int) dbh->DecompressedSize));
                    } else {
                        Array.Copy(_readBuffer, 0, _blockBuffer, 0, dbh->DecompressedSize);
                    }

                    _bufferLodIndex = i;
                    _bufferBlockIndex = j;
                    _bufferValidSize = dbh->DecompressedSize;

                    if (bufferConsumed < _bufferValidSize) {
                        var available = Math.Min((int) bufferRemaining, count);
                        Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                        offset += available;
                        count -= available;
                        PositionUint += (uint) available;
                        totalRead += available;
                        if (available == bufferRemaining) {
                            _bufferLodIndex = _bufferBlockIndex = -1;
                            _bufferValidSize = 0;
                        }

                        if (count == 0)
                            break;
                    }
                }
            }
        }

        // 3. Pad.
        totalRead += ReadImplPadTo(buffer, ref offset, ref count, (uint)Length);

        return totalRead;
    }

    public override FileType Type => FileType.Texture;

    public override object Clone() => new TextureVirtualFileStream(this);

    public TexFile.TexHeader TexHeader => _offsetManager.Header;
    
    public TextureBuffer ExtractMipmapOfSizeAtLeast(int minEdgeLength) {
        var header = _offsetManager.Header;
        var level = 0;
        while (level < header.MipLevels - 1 &&
               (header.Width >> (level + 1)) >= minEdgeLength &&
               (header.Height >> (level + 1)) >= minEdgeLength)
            level++;
        return ExtractMipmap(level);
    }

    public unsafe TextureBuffer ExtractMipmap(int level) {
        var header = _offsetManager.Header;
        if (level < 0 || level >= header.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(level), level, null);
        
        var offset = header.OffsetToSurface[level];
        var length = (int)((level == header.MipLevels - 1 ? Length : header.OffsetToSurface[level + 1]) - offset);
        var buffer = new byte[length];
        new TextureVirtualFileStream(this).WithSeek(offset).ReadFully(new(buffer));
        
        var mipWidth = Math.Max(1, header.Width >> level);
        var mipHeight = Math.Max(1, header.Height >> level);
        var mipDepth = Math.Max(1, header.Depth >> level);
        return TextureBuffer.FromTextureFormat(
            header.Type,
            header.Format,
            mipWidth,
            mipHeight,
            mipDepth,
            new[] {length},
            buffer,
            PlatformId);
    }

    private class OffsetManager {
        public readonly LuminaBinaryReader Reader;
        public readonly long BaseOffset;
        public readonly int NumLods;
        public readonly LodBlock[] Lods;
        public readonly TexFile.TexHeader Header;
        public readonly byte[] HeaderBytes;

        public unsafe OffsetManager(LuminaBinaryReader reader, long baseOffset, uint headerSize, uint numBlocks) {
            Reader = reader;
            BaseOffset = baseOffset;
            NumLods = (int) numBlocks;

            var locators = reader
                .WithSeek(BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<LodBlockStruct>(NumLods);

            var texHeaderLength = locators[0].CompressedOffset;

            Lods = new LodBlock[NumLods];
            for (var i = 0; i < NumLods; i++) {
                var baseRequestOffset = i == 0 ? texHeaderLength : Lods[i - 1].RequestOffsets[^1];
                var blockSizes = reader.ReadStructuresAsArray<ushort>((int) locators[i].BlockCount);

                Lods[i] = new(locators[i], blockSizes, baseRequestOffset, headerSize);
            }

            HeaderBytes = reader.WithSeek(BaseOffset + headerSize).ReadBytes((int) texHeaderLength);
            fixed (void* p = HeaderBytes)
                Header = *(TexFile.TexHeader*)p;
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

        public LodBlock(LodBlockStruct locator, ushort[] blockSizes, uint baseRequestOffset, uint headerSize) {
            Summary = locator;
            Sizes = blockSizes;

            RequestOffsets = new uint[locator.BlockCount + 1];
            Array.Fill(RequestOffsets, uint.MaxValue);
            RequestOffsets[0] = baseRequestOffset;
            RequestOffsets[^1] = baseRequestOffset + Summary.DecompressedSize;

            Offsets = new uint[locator.BlockCount];
            Offsets[0] = headerSize + locator.CompressedOffset;
            for (var i = 1; i < locator.BlockCount; i++)
                Offsets[i] = Offsets[i - 1] + Sizes[i - 1];

            DecompressedSizes = new ushort[locator.BlockCount];
            Array.Fill(DecompressedSizes, ushort.MaxValue);
        }
    }
#pragma warning restore CS0649
}
