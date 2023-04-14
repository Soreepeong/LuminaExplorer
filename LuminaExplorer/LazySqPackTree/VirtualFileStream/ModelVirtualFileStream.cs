using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Lumina.Data.Parsing;
using Lumina.Data.Structs;
using Lumina.Extensions;
using LuminaExplorer.Util;

namespace LuminaExplorer.LazySqPackTree.VirtualFileStream;

public class ModelVirtualFileStream : BaseVirtualFileStream {
    private const int ModelFileHeaderSize = 0x44;

    private readonly Stream _baseStream;
    private readonly long _baseOffset;

    private int _numBlocks;
    private uint[] _requestOffsets = null!;
    private uint[] _blockOffsets = null!;
    private ushort[] _blockSizes = null!;
    private ushort[] _blockDecompressedSizes = null!;
    public readonly byte[] _modelFileHeaderBytes = new byte[ModelFileHeaderSize];

    private bool _offsetsReady;

    private int _blockIndex = -1;
    private uint _blockDecompressedSize;
    private readonly byte[] _readBuffer = new byte[16384];
    private readonly byte[] _blockBuffer = new byte[16000];

    public ModelVirtualFileStream(Stream baseStream, long baseOffset, uint length,
        uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(length, reservedSpaceUnits, occupiedSpaceUnits) {
        _baseStream = baseStream;
        _baseOffset = baseOffset;
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (count == 0)
            return 0;

        PrepareOffsets();

        var totalRead = 0;

        // 0. Header
        if (_position < ModelFileHeaderSize) {
            var consumed = (int) _position;
            var remaining = ModelFileHeaderSize - consumed;
            var available = Math.Min(count, remaining);
            Array.Copy(_modelFileHeaderBytes, consumed, buffer, consumed, available);
            offset += available;
            count -= available;
            _position += (uint) available;
            totalRead += available;
            if (count == 0)
                return totalRead;
        }

        // 1. Drain previous read
        if (0 <= _blockIndex && _blockIndex < _numBlocks) {
            var bufferConsumed = (int) (Position - _requestOffsets[_blockIndex]);
            var bufferRemaining = (int) (_requestOffsets[_blockIndex + 1] - Position);
            if (bufferConsumed < _blockSizes[_blockIndex] && bufferRemaining > 0) {
                var available = Math.Min(bufferRemaining, count);
                Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                offset += available;
                count -= available;
                Position += available;
                totalRead += available;
                if (available == bufferRemaining)
                    _blockIndex = -1;
                if (count == 0)
                    return totalRead;
            }
        }

        // 2. New blocks!
        var i = Array.BinarySearch(_requestOffsets, _position);
        if (i < 0)
            i = ~i - 1;

        unsafe {
            fixed (void* p = _readBuffer) {
                var dbh = (DatBlockHeader*) p;
                var dbhSize = Marshal.SizeOf<DatBlockHeader>();

                for (; i < _numBlocks; i++) {
                    _baseStream
                        .SeekIfNecessary(_baseOffset + _blockOffsets[i])
                        .ReadFully(new(_readBuffer, 0, _blockSizes[i]));

                    if (dbh->IsCompressed) {
                        using var zlibStream = new DeflateStream(
                            new MemoryStream(_readBuffer, dbhSize, (int)dbh->CompressedSize),
                            CompressionMode.Decompress);
                        zlibStream.ReadFully(new(_blockBuffer, 0, (int)dbh->DecompressedSize));
                    } else {
                        Array.Copy(_readBuffer, 0, _blockBuffer, 0, dbh->DecompressedSize);
                    }

                    _blockIndex = i;
                    _blockDecompressedSize = dbh->DecompressedSize;

                    var bufferConsumed = _position - _requestOffsets[_blockIndex];
                    var bufferRemaining = _requestOffsets[_blockIndex + 1] - _position;
                    if (bufferConsumed < _blockDecompressedSize && bufferRemaining > 0) {
                        var available = Math.Min((int) bufferRemaining, count);
                        Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                        offset += available;
                        count -= available;
                        _position += (uint) available;
                        totalRead += available;
                        if (available == bufferRemaining) {
                            _blockIndex = -1;
                            _blockDecompressedSize = 0;
                        }

                        if (count == 0)
                            break;
                    }
                }
            }
        }

        return totalRead;
    }

    public override FileType Type => FileType.Standard;

    private unsafe void PrepareOffsets() {
        if (_offsetsReady)
            return;

        var reader = new BinaryReader(_baseStream.SeekIfNecessary(_baseOffset));
        var fileHeader = reader.ReadStructure<SqPackFileInfo>();
        var locator = reader.ReadStructure<ModelBlockLocator>();

        _numBlocks = locator.FirstBlockIndices.Index[2] + locator.BlockCount.Index[2];
        _requestOffsets = new uint[_numBlocks + 1];
        _blockOffsets = new uint[_numBlocks];
        _blockSizes = new ushort[_numBlocks];
        _blockDecompressedSizes = new ushort[_numBlocks];

        fixed (void* p = _blockSizes)
            _baseStream.ReadFully(new(p, 2 * _numBlocks));

        var modelFileHeader = new MdlStructs.ModelFileHeader {
            Version = fileHeader.NumberOfBlocks,
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

        var blockHeader = new DatBlockHeader();
        for (var i = 0; i < _numBlocks; i++) {
            _blockOffsets[i] = i == 0 ? fileHeader.Size : _blockOffsets[i - 1] + _blockSizes[i - 1];
            if (_blockOffsets[i] == Length)
                blockHeader.CompressedSize = blockHeader.DecompressedSize = 0;
            else
                _baseStream.SeekIfNecessary(_baseOffset + _blockOffsets[i])
                    .ReadFully(new(&blockHeader, Marshal.SizeOf<DatBlockHeader>()));
            _blockDecompressedSizes[i] = checked((ushort) blockHeader.DecompressedSize);
            _requestOffsets[i] = i == 0
                ? ModelFileHeaderSize
                : _requestOffsets[i - 1] + _blockDecompressedSizes[i - 1];
        }

        _requestOffsets[^1] = (uint) Length;

        for (int i = locator.FirstBlockIndices.Stack, iTo = i + locator.BlockCount.Stack; i < iTo; ++i)
            modelFileHeader.StackSize += _blockDecompressedSizes[i];
        for (int i = locator.FirstBlockIndices.Runtime, iTo = i + locator.BlockCount.Runtime; i < iTo; ++i)
            modelFileHeader.RuntimeSize += _blockDecompressedSizes[i];
        for (var j = 0; j < 3; ++j) {
            for (int i = locator.FirstBlockIndices.Vertex[j], iTo = i + locator.BlockCount.Vertex[j]; i < iTo; ++i)
                modelFileHeader.VertexBufferSize[j] += _blockDecompressedSizes[i];
            for (int i = locator.FirstBlockIndices.Index[j], iTo = i + locator.BlockCount.Index[j]; i < iTo; ++i)
                modelFileHeader.IndexBufferSize[j] += _blockDecompressedSizes[i];
            modelFileHeader.VertexOffset[j] = locator.BlockCount.Vertex[j] > 0
                ? _requestOffsets[locator.FirstBlockIndices.Vertex[j]]
                : 0;
            modelFileHeader.IndexOffset[j] = locator.BlockCount.Index[j] > 0
                ? _requestOffsets[locator.FirstBlockIndices.Index[j]]
                : 0;
        }

        using var ms = new MemoryStream(_modelFileHeaderBytes);
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

        _offsetsReady = true;
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
