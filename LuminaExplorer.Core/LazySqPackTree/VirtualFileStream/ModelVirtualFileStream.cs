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
using Lumina.Data.Parsing;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.VirtualFileStream;

public sealed class ModelVirtualFileStream : BaseVirtualFileStream {
    private const int ModelFileHeaderSize = 0x44;

    private readonly OffsetManager _offsetManager;

    private LuminaBinaryReader? _reader;

    private int _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public ModelVirtualFileStream(string datPath, PlatformId platformId, long baseOffset, ModelBlock modelBlock)
        : base(platformId, modelBlock.RawFileSize) => _offsetManager = new(datPath, platformId, baseOffset, modelBlock);

    public ModelVirtualFileStream(ModelVirtualFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) => _offsetManager = cloneFrom._offsetManager;

    ~ModelVirtualFileStream() {
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
        if (PositionUint < ModelFileHeaderSize) {
            var consumed = (int) PositionUint;
            var remaining = ModelFileHeaderSize - consumed;
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
            if (_offsetManager.RequestOffsets[_bufferBlockIndex] <= PositionUint &&
                PositionUint < _offsetManager.RequestOffsets[_bufferBlockIndex + 1]) {
                var bufferConsumed = (int) (Position - _offsetManager.RequestOffsets[_bufferBlockIndex]);
                var bufferRemaining = (int) (_offsetManager.RequestOffsets[_bufferBlockIndex + 1] - Position);
                if (bufferConsumed < _offsetManager.BlockSizes[_bufferBlockIndex] && bufferRemaining > 0) {
                    var available = Math.Min(bufferRemaining, count);
                    Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    Position += available;
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

                readBuffer = ArrayPool<byte>.Shared.RentAsNecessary(readBuffer, 16384);
                await (_reader ??= _offsetManager.CreateNewReader())
                    .WithSeek(_offsetManager.BaseOffset + _offsetManager.BlockOffsets[i])
                    .BaseStream.ReadExactlyAsync(new(readBuffer, 0, _offsetManager.BlockSizes[i]), cancellationToken);

                DatBlockHeader dbh;
                unsafe {
                    fixed (void* p = readBuffer)
                        dbh = *(DatBlockHeader*) p;
                }

                cancellationToken.ThrowIfCancellationRequested();

                _blockBuffer = ArrayPool<byte>.Shared.RentAsNecessary(_blockBuffer, (int) dbh.DecompressedSize);
                if (dbh.IsCompressed) {
                    unsafe {
                        fixed (byte* b1 = &readBuffer[Unsafe.SizeOf<DatBlockHeader>()]) {
                            using var s1 = new DeflateStream(new UnmanagedMemoryStream(b1, dbh.CompressedSize),
                                CompressionMode.Decompress);
                            s1.ReadExactly(new(_blockBuffer, 0, (int) dbh.DecompressedSize));
                        }
                    }
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

    public override BaseVirtualFileStream Clone(bool keepOpen) => new ModelVirtualFileStream(this);

    protected override void Dispose(bool disposing) {
        CloseButOpenAgainWhenNecessary();
        base.Dispose(disposing);
    }

    public override void CloseButOpenAgainWhenNecessary() {
        SafeDispose.One(ref _reader);
    }

    private class OffsetManager : BaseOffsetManager {
        public readonly int NumBlocks;
        public readonly uint[] RequestOffsets;
        public readonly uint[] BlockOffsets;
        public readonly ushort[] BlockSizes;
        public readonly byte[] HeaderBytes;

        public unsafe OffsetManager(string datPath, PlatformId platformId, long baseOffset, ModelBlock modelBlock) :
            base(datPath, platformId, baseOffset) {
            var fileInfo = *(SqPackFileInfo*) &modelBlock;
            var locator = *(ModelBlockLocator*) ((byte*) &modelBlock + Unsafe.SizeOf<SqPackFileInfo>());

            var underlyingSize = (long) fileInfo.__unknown[0] << 7;

            NumBlocks = locator.FirstBlockIndices.Index[2] + locator.BlockCount.Index[2];
            RequestOffsets = new uint[NumBlocks + 1];
            BlockOffsets = new uint[NumBlocks];
            var blockDecompressedSizes = new ushort[NumBlocks];
            HeaderBytes = new byte[ModelFileHeaderSize];

            using var reader = CreateNewReader();
            BlockSizes = reader.WithSeek(BaseOffset + Unsafe.SizeOf<ModelBlock>())
                .ReadStructuresAsArray<ushort>(NumBlocks);

            var modelFileHeader = new MdlStructs.ModelFileHeader {
                Version = fileInfo.NumberOfBlocks,
                VertexDeclarationCount = locator.VertexDeclarationCount,
                MaterialCount = locator.MaterialCount,
                LodCount = locator.LodCount,
                EnableIndexBufferStreaming = locator.EnableIndexBufferStreaming,
                EnableEdgeGeometry = locator.EnableEdgeGeometry,
                VertexBufferSize = new uint[3],
                IndexBufferSize = new uint[3],
                VertexOffset = new uint[3],
                IndexOffset = new uint[3],
            };

            for (var i = 0; i < NumBlocks; i++) {
                BlockOffsets[i] = i == 0 ? fileInfo.Size : BlockOffsets[i - 1] + BlockSizes[i - 1];
                if (BlockOffsets[i] == underlyingSize) {
                    blockDecompressedSizes[i] = 0;
                } else {
                    var blockHeader = reader.WithSeek(BaseOffset + BlockOffsets[i]).ReadStructure<DatBlockHeader>();
                    blockDecompressedSizes[i] = checked((ushort) blockHeader.DecompressedSize);
                }

                RequestOffsets[i] = i == 0
                    ? ModelFileHeaderSize
                    : RequestOffsets[i - 1] + blockDecompressedSizes[i - 1];
            }

            RequestOffsets[^1] = modelBlock.RawFileSize;

            for (int i = locator.FirstBlockIndices.Stack, iTo = i + locator.BlockCount.Stack; i < iTo; ++i)
                modelFileHeader.StackSize += blockDecompressedSizes[i];
            for (int i = locator.FirstBlockIndices.Runtime, iTo = i + locator.BlockCount.Runtime; i < iTo; ++i)
                modelFileHeader.RuntimeSize += blockDecompressedSizes[i];
            for (var j = 0; j < 3; ++j) {
                for (int i = locator.FirstBlockIndices.Vertex[j], iTo = i + locator.BlockCount.Vertex[j]; i < iTo; ++i)
                    modelFileHeader.VertexBufferSize[j] += blockDecompressedSizes[i];
                for (int i = locator.FirstBlockIndices.Index[j], iTo = i + locator.BlockCount.Index[j]; i < iTo; ++i)
                    modelFileHeader.IndexBufferSize[j] += blockDecompressedSizes[i];
                modelFileHeader.VertexOffset[j] = locator.BlockCount.Vertex[j] > 0
                    ? RequestOffsets[locator.FirstBlockIndices.Vertex[j]]
                    : 0;
                modelFileHeader.IndexOffset[j] = locator.BlockCount.Index[j] > 0
                    ? RequestOffsets[locator.FirstBlockIndices.Index[j]]
                    : 0;
            }

            using var ms = new MemoryStream(HeaderBytes);
            ms.Seek(0, SeekOrigin.Begin);
            ms.Write(BitConverter.GetBytes(modelFileHeader.Version));
            ms.Write(BitConverter.GetBytes(modelFileHeader.StackSize));
            ms.Write(BitConverter.GetBytes(modelFileHeader.RuntimeSize));
            ms.Write(BitConverter.GetBytes(modelFileHeader.VertexDeclarationCount));
            ms.Write(BitConverter.GetBytes(modelFileHeader.MaterialCount));
            for (var i = 0; i < 3; i++)
                ms.Write(BitConverter.GetBytes(modelFileHeader.VertexOffset[i]));
            for (var i = 0; i < 3; i++)
                ms.Write(BitConverter.GetBytes(modelFileHeader.IndexOffset[i]));
            for (var i = 0; i < 3; i++)
                ms.Write(BitConverter.GetBytes(modelFileHeader.VertexBufferSize[i]));
            for (var i = 0; i < 3; i++)
                ms.Write(BitConverter.GetBytes(modelFileHeader.IndexBufferSize[i]));
            ms.Write(new[] {modelFileHeader.LodCount});
            ms.Write(BitConverter.GetBytes(modelFileHeader.EnableIndexBufferStreaming));
            ms.Write(BitConverter.GetBytes(modelFileHeader.EnableEdgeGeometry));
            ms.Write(new byte[] {0});
        }

#pragma warning disable CS0649
        [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [StructLayout(LayoutKind.Sequential)]
        private struct ModelBlockLocator {
            public static readonly int[] EntryIndexMap = {0, 1, 2, 5, 8, 3, 6, 9, 4, 7, 10,};

            public ChunkInfo32 AlignedDecompressedSizes;
            public ChunkInfo32 ChunkSizes;
            public ChunkInfo32 FirstBlockOffsets;
            public ChunkInfo16 FirstBlockIndices;
            public ChunkInfo16 BlockCount;
            public ushort VertexDeclarationCount;
            public ushort MaterialCount;
            public byte LodCount;
            public bool EnableIndexBufferStreaming;
            public bool EnableEdgeGeometry;
            public byte Padding;

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct ChunkInfo16 {
                public fixed ushort Entries[11];

                public ushort StructOrder(int index) => Entries[index];

                public ushort DataOrder(int index) => StructOrder(EntryIndexMap[index]);

                public ushort Stack => StructOrder(0);

                public ushort Runtime => StructOrder(1);

                public ushort[] Vertex => new[] {StructOrder(2), StructOrder(3), StructOrder(4)};

                public ushort[] EdgeGeometryVertex => new[] {StructOrder(5), StructOrder(6), StructOrder(7)};

                public ushort[] Index => new[] {StructOrder(8), StructOrder(9), StructOrder(10)};
            }

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct ChunkInfo32 {
                public fixed uint Entries[11];

                public uint StructOrder(int index) => Entries[index];

                public uint DataOrder(int index) => StructOrder(EntryIndexMap[index]);

                public uint Stack => StructOrder(0);

                public uint Runtime => StructOrder(1);

                public uint[] Vertex => new[] {StructOrder(2), StructOrder(3), StructOrder(4)};

                public uint[] EdgeGeometryVertex => new[] {StructOrder(5), StructOrder(6), StructOrder(7)};

                public uint[] Index => new[] {StructOrder(8), StructOrder(9), StructOrder(10)};
            }
        }
#pragma warning restore CS0649
    }
}
