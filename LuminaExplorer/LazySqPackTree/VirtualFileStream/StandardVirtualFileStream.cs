using System.IO.Compression;
using System.Runtime.InteropServices;
using Lumina.Data.Structs;
using LuminaExplorer.Util;

namespace LuminaExplorer.LazySqPackTree.VirtualFileStream;

public class StandardVirtualFileStream : BaseVirtualFileStream {
    private readonly Stream _baseStream;
    private readonly long _baseOffset;
    private readonly uint _headerSize;
    private readonly int _numBlocks;
    private readonly uint[] _requestOffsets;
    private readonly uint[] _blockOffsets;
    private readonly ushort[] _blockSizes;

    private bool _offsetsReady;

    private int _blockIndex = -1;
    private uint _blockDecompressedSize;
    private readonly byte[] _readBuffer = new byte[16384];
    private readonly byte[] _blockBuffer = new byte[16000];

    public StandardVirtualFileStream(Stream baseStream, long baseOffset, uint headerSize, uint numBlocks, uint length,
        uint reservedSpaceUnits, uint occupiedSpaceUnits)
        : base(length, reservedSpaceUnits, occupiedSpaceUnits) {
        _baseStream = baseStream;
        _baseOffset = baseOffset;
        _headerSize = headerSize;
        _numBlocks = (int) numBlocks;
        _requestOffsets = new uint[numBlocks + 1];
        _requestOffsets[^1] = length;
        _blockOffsets = new uint[numBlocks];
        _blockSizes = new ushort[numBlocks];
    }

    public override int Read(byte[] buffer, int offset, int count) {
        if (count == 0)
            return 0;

        PrepareOffsets();

        var totalRead = 0;

        // 1. Drain previous read
        if (0 <= _blockIndex && _blockIndex < _numBlocks) {
            var bufferConsumed = (_position - _requestOffsets[_blockIndex]);
            var bufferRemaining = (_requestOffsets[_blockIndex + 1] - _position);
            if (bufferConsumed < _blockDecompressedSize && bufferRemaining > 0) {
                var available = Math.Min((int)bufferRemaining, count);
                Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                offset += available;
                count -= available;
                _position += (uint)available;
                totalRead += available;
                if (available == bufferRemaining) {
                    _blockIndex = -1;
                    _blockDecompressedSize = 0;
                }

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
                        var available = Math.Min((int)bufferRemaining, count);
                        Array.Copy(_blockBuffer, bufferConsumed, buffer, offset, available);
                        offset += available;
                        count -= available;
                        _position += (uint)available;
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

        var blockInfos = new DatStdFileBlockInfos[_numBlocks];
        fixed (void* p = blockInfos) {
            _baseStream
                .SeekIfNecessary(_baseOffset + (uint) Marshal.SizeOf<SqPackFileInfo>())
                .ReadFully(new(p, Marshal.SizeOf<DatStdFileBlockInfos>() * _numBlocks));
        }

        for (var i = 0; i < _numBlocks; i++) {
            _requestOffsets[i] = i == 0 ? 0 : _requestOffsets[i - 1] + blockInfos[i - 1].UncompressedSize;
            _blockSizes[i] = blockInfos[i].CompressedSize;
            _blockOffsets[i] = _headerSize + blockInfos[i].Offset;
        }

        _offsetsReady = true;
    }
}