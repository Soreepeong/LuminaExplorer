using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Shared block layout of .eqp and .gmp files.
/// <code>
/// [ControlBlock:ulong]                   bit N set = block N is present in the file
/// 159 x [Entry:ulong]                    remainder of block 0 (entry 0 is the control block)
/// (PopCount(ControlBlock) - 1) x 160 x [Entry:ulong]
/// </code>
/// Present blocks are stored in ascending order; absent blocks take a format-specific default value.
/// Set ID 0 does not exist and is redirected to set ID 1.
/// </summary>
/// <remarks>Not a FileResource; it is only a helper for <see cref="EqpFile"/> and <see cref="GmpFile"/>.</remarks>
internal static class EqpGmpBlockTable {
    public const int BlockSize = 160;
    public const int NumBlocks = 64;
    public const int EntrySize = 8;
    public const int EntryCount = BlockSize * NumBlocks;

    /// <summary>Expands the file into one entry per set ID (0 to <see cref="EntryCount"/> - 1).</summary>
    public static ulong[] Expand(ReadOnlySpan<byte> data, ulong emptyValue, out ulong controlBlock)
    {
        if (data.Length < EntrySize)
            throw new InvalidDataException("File is too small to contain the control block.");

        var raw = MemoryMarshal.Cast<byte, ulong>(data[..(data.Length / EntrySize * EntrySize)]);
        controlBlock = raw[0];

        var expanded = new ulong[EntryCount];
        var storedBlocks = 0;
        for (var i = 0; i < NumBlocks; i++) {
            var target = expanded.AsSpan(i * BlockSize, BlockSize);
            if (((controlBlock >> i) & 1) == 0) {
                target.Fill(emptyValue);
                continue;
            }

            var sourceOffset = storedBlocks++ * BlockSize;
            if (sourceOffset + BlockSize > raw.Length)
                throw new InvalidDataException($"Block {i} lies beyond the end of the file.");
            raw.Slice(sourceOffset, BlockSize).CopyTo(target);
        }

        // Entry 0 is occupied by the control block in the file; the game redirects set ID 0 to 1.
        expanded[0] = expanded[1];
        return expanded;
    }

    public static bool IsBlockPresent(ulong controlBlock, int blockIndex) =>
        blockIndex is >= 0 and < NumBlocks && ((controlBlock >> blockIndex) & 1) != 0;

    public static bool IsSetIdPresent(ulong controlBlock, int setId) =>
        setId is >= 0 and < EntryCount && IsBlockPresent(controlBlock, Math.Max(setId, 1) / BlockSize);
}
