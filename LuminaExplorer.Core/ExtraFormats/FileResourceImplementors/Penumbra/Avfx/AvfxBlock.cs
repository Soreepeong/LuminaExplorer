using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Avfx;

/// <summary>A block in an AVFX file: a name, a size, and data padded to a multiple of 4 bytes.</summary>
public sealed class AvfxBlock {
    /// <summary>Name of this block. See <see cref="AvfxBlockName"/>.</summary>
    public readonly uint Name;

    /// <summary>Offset of this block (its header), relative to the data it has been parsed from.</summary>
    public readonly int Offset;

    /// <summary>Data of this block, excluding the padding.</summary>
    public readonly byte[] Data;

    private IReadOnlyList<AvfxBlock>? _children;
    private bool _childrenParsed;

    private AvfxBlock(uint name, int offset, byte[] data)
    {
        this.Name = name;
        this.Offset = offset;
        this.Data = data;
    }

    /// <summary>Name of this block as a string.</summary>
    public string NameString => AvfxBlockName.ToString(this.Name);

    /// <summary>Size of the data, excluding the padding.</summary>
    public int Size => this.Data.Length;

    /// <summary>Size of the block, including the header and the padding.</summary>
    public int PaddedSize => 8 + ((this.Data.Length + 3) & ~3);

    /// <summary>Sub-blocks, if the data of this block looks like a sequence of blocks; otherwise null.</summary>
    /// <remarks>Determined heuristically: every sub-block must have a plausible name, and the sub-blocks must exactly
    /// cover the data.</remarks>
    public IReadOnlyList<AvfxBlock>? Children {
        get {
            if (!this._childrenParsed) {
                this._children = TryParseSequence(this.Data, out var children) ? children : null;
                this._childrenParsed = true;
            }

            return this._children;
        }
    }

    public bool ToBoolean() => this.Data.Length >= 1 && this.Data[0] != 0;

    public uint ToUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(this.Data);

    public int ToInt32() => BinaryPrimitives.ReadInt32LittleEndian(this.Data);

    public float ToSingle() => BinaryPrimitives.ReadSingleLittleEndian(this.Data);

    /// <summary>Interprets the data as a null-terminated UTF-8 string.</summary>
    public string ToStringValue()
    {
        var span = this.Data.AsSpan();
        var length = span.IndexOf((byte) 0);
        return Encoding.UTF8.GetString(length < 0 ? span : span[..length]);
    }

    /// <summary>Finds the first sub-block with the given name.</summary>
    public AvfxBlock? FindChild(uint name)
    {
        if (this.Children is not { } children)
            return null;
        foreach (var child in children) {
            if (child.Name == name)
                return child;
        }

        return null;
    }

    public override string ToString() => $"{this.NameString} ({this.Size} bytes)";

    /// <summary>Parses a block at the given offset.</summary>
    /// <exception cref="InvalidDataException">If the block does not fit in the data.</exception>
    public static AvfxBlock Parse(ReadOnlySpan<byte> data, int offset)
    {
        if (!TryParse(data, offset, out var block))
            throw new InvalidDataException($"AVFX block at 0x{offset:X} does not fit in the data.");
        return block;
    }

    /// <summary>Attempts to parse a block at the given offset.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, int offset, out AvfxBlock block)
    {
        block = null!;
        if (offset < 0 || offset > data.Length - 8)
            return false;

        var name = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
        if (size > data.Length - offset - 8)
            return false;

        block = new(name, offset, data.Slice(offset + 8, (int) size).ToArray());
        return true;
    }

    /// <summary>Attempts to parse the entire data as a sequence of blocks with plausible names.</summary>
    public static bool TryParseSequence(ReadOnlySpan<byte> data, out List<AvfxBlock> blocks)
    {
        blocks = new();
        var offset = 0;
        while (offset < data.Length) {
            if (!TryParse(data, offset, out var block) || !AvfxBlockName.IsPlausible(block.Name))
                return false;

            blocks.Add(block);
            offset += block.PaddedSize;
        }

        // The padding of the last block may be omitted.
        return blocks.Count > 0;
    }
}