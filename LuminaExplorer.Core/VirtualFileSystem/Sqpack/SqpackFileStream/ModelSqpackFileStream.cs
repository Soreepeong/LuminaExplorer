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

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

public sealed class ModelSqpackFileStream : BaseSqpackFileStream {
    private const int ModelFileHeaderSize = 0x44;

    private readonly OffsetManager _offsetManager;

    private LuminaBinaryReader? _reader;

    private int _bufferBlockIndex = -1;
    private uint _bufferValidSize;
    private byte[]? _blockBuffer;

    public ModelSqpackFileStream(string datPath, PlatformId platformId, long baseOffset, ModelBlock modelBlock)
        : base(platformId, modelBlock.RawFileSize) =>
        this._offsetManager = new(datPath, platformId, baseOffset, modelBlock);

    public ModelSqpackFileStream(ModelSqpackFileStream cloneFrom)
        : base(cloneFrom.PlatformId, (uint) cloneFrom.Length) =>
        this._offsetManager = cloneFrom._offsetManager;

    ~ModelSqpackFileStream()
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

        // 0. Header
        if (this.PositionUint < ModelFileHeaderSize) {
            var consumed = (int) this.PositionUint;
            var remaining = ModelFileHeaderSize - consumed;
            var available = Math.Min(count, remaining);
            Array.Copy(this._offsetManager.HeaderBytes, consumed, buffer, offset, available);
            offset += available;
            count -= available;
            this.PositionUint += (uint) available;
            totalRead += available;
            if (count == 0)
                return totalRead;
        }

        // 1. Drain previous read
        if (this._blockBuffer is not null) {
            if (this._offsetManager.RequestOffsets[this._bufferBlockIndex] <= this.PositionUint &&
                this.PositionUint < this._offsetManager.RequestOffsets[this._bufferBlockIndex + 1]) {
                var bufferConsumed = (int) (this.Position - this._offsetManager.RequestOffsets[this._bufferBlockIndex]);
                var bufferRemaining =
                    (int) (this._offsetManager.RequestOffsets[this._bufferBlockIndex + 1] - this.Position);
                if (bufferConsumed < this._offsetManager.BlockSizes[this._bufferBlockIndex] && bufferRemaining > 0) {
                    var available = Math.Min(bufferRemaining, count);
                    Array.Copy(this._blockBuffer, bufferConsumed, buffer, offset, available);
                    offset += available;
                    count -= available;
                    this.Position += available;
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

    public override BaseSqpackFileStream Clone(bool keepOpen) => new ModelSqpackFileStream(this);

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
        public readonly byte[] HeaderBytes;

        public unsafe OffsetManager(string datPath, PlatformId platformId, long baseOffset, ModelBlock modelBlock) :
            base(datPath, platformId, baseOffset)
        {
            var fileInfo = *(SqPackFileInfo*) &modelBlock;
            var locator = *(ModelBlockLocator*) ((byte*) &modelBlock + Unsafe.SizeOf<SqPackFileInfo>());

            var underlyingSize = (long) fileInfo.__unknown[0] << 7;

            this.NumBlocks = locator.FirstBlockIndices.Index[2] + locator.BlockCount.Index[2];
            this.RequestOffsets = new uint[this.NumBlocks + 1];
            this.BlockOffsets = new uint[this.NumBlocks];
            var blockDecompressedSizes = new ushort[this.NumBlocks];
            this.HeaderBytes = new byte[ModelFileHeaderSize];

            using var reader = this.CreateNewReader();
            this.BlockSizes = reader.WithSeek(this.BaseOffset + Unsafe.SizeOf<ModelBlock>())
                .ReadStructuresAsArray<ushort>(this.NumBlocks);

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

            for (var i = 0; i < this.NumBlocks; i++) {
                this.BlockOffsets[i] = i == 0 ? fileInfo.Size : this.BlockOffsets[i - 1] + this.BlockSizes[i - 1];
                if (this.BlockOffsets[i] == underlyingSize) {
                    blockDecompressedSizes[i] = 0;
                } else {
                    var blockHeader = reader.WithSeek(this.BaseOffset + this.BlockOffsets[i])
                        .ReadStructure<DatBlockHeader>();
                    blockDecompressedSizes[i] = checked((ushort) blockHeader.DecompressedSize);
                }

                this.RequestOffsets[i] = i == 0
                    ? ModelFileHeaderSize
                    : this.RequestOffsets[i - 1] + blockDecompressedSizes[i - 1];
            }

            this.RequestOffsets[^1] = modelBlock.RawFileSize;

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
                    ? this.RequestOffsets[locator.FirstBlockIndices.Vertex[j]]
                    : 0;
                modelFileHeader.IndexOffset[j] = locator.BlockCount.Index[j] > 0
                    ? this.RequestOffsets[locator.FirstBlockIndices.Index[j]]
                    : 0;
            }

            using var ms = new MemoryStream(this.HeaderBytes);
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
            ms.Write(new[] { modelFileHeader.LodCount });
            ms.Write(BitConverter.GetBytes(modelFileHeader.EnableIndexBufferStreaming));
            ms.Write(BitConverter.GetBytes(modelFileHeader.EnableEdgeGeometry));
            ms.Write([0]);
        }

#pragma warning disable CS0649
        [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [StructLayout(LayoutKind.Sequential)]
        private struct ModelBlockLocator {
            public static readonly int[] EntryIndexMap = [0, 1, 2, 5, 8, 3, 6, 9, 4, 7, 10];

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

                public ushort StructOrder(int index) => this.Entries[index];

                public ushort DataOrder(int index) => this.StructOrder(EntryIndexMap[index]);

                public ushort Stack => this.StructOrder(0);

                public ushort Runtime => this.StructOrder(1);

                public ushort[] Vertex => [this.StructOrder(2), this.StructOrder(3), this.StructOrder(4)];

                public ushort[] EdgeGeometryVertex => [this.StructOrder(5), this.StructOrder(6), this.StructOrder(7)];

                public ushort[] Index => [this.StructOrder(8), this.StructOrder(9), this.StructOrder(10)];
            }

            [StructLayout(LayoutKind.Sequential)]
            public unsafe struct ChunkInfo32 {
                public fixed uint Entries[11];

                public uint StructOrder(int index) => this.Entries[index];

                public uint DataOrder(int index) => this.StructOrder(EntryIndexMap[index]);

                public uint Stack => this.StructOrder(0);

                public uint Runtime => this.StructOrder(1);

                public uint[] Vertex => [this.StructOrder(2), this.StructOrder(3), this.StructOrder(4)];

                public uint[] EdgeGeometryVertex => [this.StructOrder(5), this.StructOrder(6), this.StructOrder(7)];

                public uint[] Index => [this.StructOrder(8), this.StructOrder(9), this.StructOrder(10)];
            }
        }
#pragma warning restore CS0649
    }
}
