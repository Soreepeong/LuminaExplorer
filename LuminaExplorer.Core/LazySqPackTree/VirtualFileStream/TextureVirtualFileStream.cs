using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public sealed class TextureVirtualFileStream : BaseVirtualFileStream {
    private readonly bool _keepOpen;

    private OffsetManager? _offsetManager;

    private int _bufferLodIndex = -1, _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public TextureVirtualFileStream(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info,
        bool keepOpen)
        : base(platformId, info.RawFileSize) {
        _keepOpen = keepOpen;
        _offsetManager = new(datPath, platformId, baseOffset, info);
        if (keepOpen)
            _offsetManager.AddRefKeepOpen();
        else
            FreeUnnecessaryResources();
    }

    public TextureVirtualFileStream(TextureVirtualFileStream cloneFrom, bool keepOpen)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) {
        _keepOpen = keepOpen;
        _offsetManager = cloneFrom._offsetManager;
        if (_offsetManager is null)
            throw new ObjectDisposedException(nameof(ModelVirtualFileStream));

        _offsetManager.AddRef();
        if (keepOpen)
            _offsetManager.AddRefKeepOpen();
    }

    ~TextureVirtualFileStream() {
        Dispose(false);
    }

    public override async Task<int>
        ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
        if (_offsetManager is null)
            throw new ObjectDisposedException(nameof(ModelVirtualFileStream));

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
        if (_blockBuffer is not null) {
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
                    if (available == bufferRemaining) {
                        _bufferBlockIndex = _bufferLodIndex = -1;
                        _bufferValidSize = 0;
                        ArrayPool<byte>.Shared.Return(ref _blockBuffer);
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
            for (var i = 0; i < _offsetManager.NumLods && count > 0; i++) {
                cancellationToken.ThrowIfCancellationRequested();

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
                    cancellationToken.ThrowIfCancellationRequested();
                    if (lod.RequestOffsets[j + 1] <= PositionUint && lod.RequestOffsets[j] != uint.MaxValue)
                        continue;

                    await _offsetManager.ReaderLock.WaitAsync(cancellationToken);
                    readBuffer = ArrayPool<byte>.Shared.RentAsNecessary(readBuffer, 16384);
                    await _offsetManager.Reader
                        .WithSeek(_offsetManager.BaseOffset + lod.Offsets[j])
                        .BaseStream.ReadExactlyAsync(new(readBuffer, 0, lod.Sizes[j]), cancellationToken);
                    _offsetManager.ReaderLock.Release();

                    DatBlockHeader dbh;
                    unsafe {
                        fixed (void* p = readBuffer)
                            dbh = *(DatBlockHeader*) p;
                    }

                    lod.DecompressedSizes[j] = checked((ushort) dbh.DecompressedSize);
                    lod.RequestOffsets[j + 1] = lod.RequestOffsets[j] + lod.DecompressedSizes[j];

                    if (lod.RequestOffsets[j + 1] <= PositionUint)
                        continue;

                    var bufferConsumed = PositionUint - lod.RequestOffsets[j];
                    var bufferRemaining = lod.RequestOffsets[j + 1] - PositionUint;

                    cancellationToken.ThrowIfCancellationRequested();

                    _blockBuffer = ArrayPool<byte>.Shared.RentAsNecessary(_blockBuffer, (int) dbh.DecompressedSize);
                    if (dbh.IsCompressed) {
                        await using var zlibStream = new DeflateStream(
                            new MemoryStream(readBuffer, Unsafe.SizeOf<DatBlockHeader>(), (int) dbh.CompressedSize),
                            CompressionMode.Decompress);
                        zlibStream.ReadExactly(new(_blockBuffer, 0, (int) dbh.DecompressedSize));
                    } else {
                        Array.Copy(readBuffer, 0, _blockBuffer, 0, dbh.DecompressedSize);
                    }

                    _bufferLodIndex = i;
                    _bufferBlockIndex = j;
                    _bufferValidSize = dbh.DecompressedSize;

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
        } finally {
            ArrayPool<byte>.Shared.Return(ref readBuffer);
            if (_bufferValidSize == 0)
                ArrayPool<byte>.Shared.Return(ref _blockBuffer);
        }

        // 3. Pad.
        totalRead += ReadImplPadTo(buffer, ref offset, ref count, (uint) Length);

        return totalRead;
    }

    public override BaseVirtualFileStream Clone(bool keepOpen) => new TextureVirtualFileStream(this, keepOpen);

    protected override void Dispose(bool disposing) {
        _offsetManager?.DecRef();
        if (_keepOpen)
            _offsetManager?.DecRefKeepOpen();
        _offsetManager = null;
        base.Dispose(disposing);
    }

    public TexFile.TexHeader TexHeader =>
        _offsetManager?.Header ?? throw new ObjectDisposedException(nameof(TextureVirtualFileStream));

    public override void FreeUnnecessaryResources() => _offsetManager?.CloseReaderIfUnnecessary();

    private class OffsetManager : BaseOffsetManager {
        public readonly int NumLods;
        public readonly LodBlock[] Lods;
        public readonly TexFile.TexHeader Header;
        public readonly byte[] HeaderBytes;

        public unsafe OffsetManager(string datPath, PlatformId platformId, long baseOffset, SqPackFileInfo info)
            : base(datPath, platformId, baseOffset) {
            NumLods = (int) info.NumberOfBlocks;

            var locators = Reader
                .WithSeek(BaseOffset + (uint) Unsafe.SizeOf<SqPackFileInfo>())
                .ReadStructuresAsArray<LodBlockStruct>(NumLods);

            var texHeaderLength = locators[0].CompressedOffset;

            Lods = new LodBlock[NumLods];
            for (var i = 0; i < NumLods; i++) {
                var baseRequestOffset = i == 0 ? texHeaderLength : Lods[i - 1].RequestOffsets[^1];
                var blockSizes = Reader.ReadStructuresAsArray<ushort>((int) locators[i].BlockCount);

                Lods[i] = new(locators[i], blockSizes, baseRequestOffset, info.Size);
            }

            HeaderBytes = Reader.WithSeek(BaseOffset + info.Size).ReadBytes((int) texHeaderLength);
            fixed (void* p = HeaderBytes)
                Header = *(TexFile.TexHeader*) p;
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
