using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;

/// <summary>A single entry of a TMB (TMLB) timeline. Unrecognized entries are represented using this class.</summary>
/// <remarks>Offsets stored inside an entry are relative to <see cref="BodyOffset"/>.</remarks>
public class TmbEntry {
    /// <summary>Magic of this entry.</summary>
    public readonly uint Magic;

    /// <summary>Offset of this entry, relative to the beginning of the timeline data.</summary>
    public readonly int Offset;

    /// <summary>Size of this entry, including the 8-byte magic and size header.</summary>
    public readonly int Size;

    /// <summary>Bytes of this entry, excluding the 8-byte magic and size header.</summary>
    public readonly byte[] Body;

    internal TmbEntry(ReadOnlySpan<byte> data, int offset)
    {
        this.Offset = offset;
        this.Magic = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        this.Size = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
        this.Body = data.Slice(offset + 8, this.Size - 8).ToArray();
    }

    /// <summary>Offset of the body of this entry, relative to the beginning of the timeline data.</summary>
    public int BodyOffset => this.Offset + 8;

    /// <summary>Magic of this entry as a string.</summary>
    public string MagicString => TmbMagic.ToString(this.Magic);

    /// <summary>Game path referenced by this entry, including the extension if implied by the entry type.</summary>
    public virtual string? GamePath => null;

    public override string ToString() => this.MagicString;

    /// <summary>Parses an entry at the given offset. The caller must ensure that the entry fits in the data.</summary>
    internal static TmbEntry Parse(ReadOnlySpan<byte> data, int offset)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        var size = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
        return magic switch {
            TmbMagic.Tmdh when size >= 16 => new TmbHeaderEntry(data, offset),
            TmbMagic.Tmpp when size >= 12 => new TmbPapPathEntry(data, offset),
            TmbMagic.Tmal when size >= 16 => new TmbActorListEntry(data, offset),
            TmbMagic.Tmac when size >= 28 => new TmbActorEntry(data, offset),
            TmbMagic.Tmtr when size >= 24 => new TmbTrackEntry(data, offset),
            _ when size >= 12 && TmbMagic.TryGetEventCode(magic, out var code) =>
                TmbEventEntry.Create(data, offset, code, size),
            _ => new TmbEntry(data, offset),
        };
    }

    protected short ReadInt16(int bodyOffset) => BinaryPrimitives.ReadInt16LittleEndian(this.Body.AsSpan(bodyOffset));

    protected ushort ReadUInt16(int bodyOffset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(this.Body.AsSpan(bodyOffset));

    protected int ReadInt32(int bodyOffset) => BinaryPrimitives.ReadInt32LittleEndian(this.Body.AsSpan(bodyOffset));

    protected uint ReadUInt32(int bodyOffset) => BinaryPrimitives.ReadUInt32LittleEndian(this.Body.AsSpan(bodyOffset));

    protected float ReadSingle(int bodyOffset) => BinaryPrimitives.ReadSingleLittleEndian(this.Body.AsSpan(bodyOffset));

    /// <summary>Reads a null-terminated string pointed by the offset stored at the given body offset.</summary>
    protected string ReadReferencedString(ReadOnlySpan<byte> data, int offsetFieldOffset)
    {
        var relative = this.ReadUInt32(offsetFieldOffset);
        var start = (long) this.BodyOffset + relative;
        if (relative == 0 || start >= data.Length)
            return string.Empty;

        var span = data[(int) start..];
        var length = span.IndexOf((byte) 0);
        return Encoding.UTF8.GetString(length < 0 ? span : span[..length]);
    }

    /// <summary>Reads an array pointed by the offset and count pair stored at the given body offsets.</summary>
    protected T[] ReadReferencedArray<T>(ReadOnlySpan<byte> data, int offsetFieldOffset, int countFieldOffset)
        where T : unmanaged =>
        this.ReadArray<T>(data, this.ReadUInt32(offsetFieldOffset), this.ReadUInt32(countFieldOffset));

    /// <summary>Reads an array located at the given offset relative to <see cref="BodyOffset"/>.</summary>
    /// <returns>The array, or an empty array if the range is empty or out of bounds.</returns>
    protected T[] ReadArray<T>(ReadOnlySpan<byte> data, uint relativeOffset, uint count) where T : unmanaged
    {
        if (count == 0)
            return [];

        var start = (long) this.BodyOffset + relativeOffset;
        var length = (long) count * Unsafe.SizeOf<T>();
        if (start + length > data.Length)
            return [];

        return MemoryMarshal.Cast<byte, T>(data.Slice((int) start, (int) length)).ToArray();
    }
}

/// <summary>An entry that starts with an ID and a time (TMAC, TMTR, and C### events).</summary>
public abstract class TmbIdentifiedEntry : TmbEntry {
    /// <summary>ID of this entry, referenced from <see cref="TmbActorListEntry"/>, <see cref="TmbActorEntry"/>, or
    /// <see cref="TmbTrackEntry"/>.</summary>
    public readonly short Id;

    /// <summary>Start time, in frames (30 per second).</summary>
    public readonly short Time;

    internal TmbIdentifiedEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Id = this.ReadInt16(0);
        this.Time = this.ReadInt16(2);
    }

    public override string ToString() => $"{this.MagicString}#{this.Id}@{this.Time}";
}

/// <summary>TMDH: timeline header.</summary>
public sealed class TmbHeaderEntry : TmbEntry {
    public readonly short Id;
    public readonly short Unknown1;
    public readonly short Length;
    public readonly short Unknown2;

    internal TmbHeaderEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Id = this.ReadInt16(0);
        this.Unknown1 = this.ReadInt16(2);
        this.Length = this.ReadInt16(4);
        this.Unknown2 = this.ReadInt16(6);
    }
}

/// <summary>TMPP: path to the .pap file for this timeline.</summary>
public sealed class TmbPapPathEntry : TmbEntry {
    /// <summary>Path as stored in the file, without the extension.</summary>
    public readonly string Path;

    internal TmbPapPathEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Path = this.ReadReferencedString(data, 0);
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path + ".pap";
}

/// <summary>TMAL: list of actors.</summary>
public sealed class TmbActorListEntry : TmbEntry {
    /// <summary>IDs of <see cref="TmbActorEntry"/>.</summary>
    public readonly short[] ActorIds;

    internal TmbActorListEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.ActorIds = this.ReadReferencedArray<short>(data, 0, 4);
    }
}

/// <summary>TMAC: an actor, consisting of tracks.</summary>
public sealed class TmbActorEntry : TmbIdentifiedEntry {
    public readonly int AbilityDelay;
    public readonly int Unknown1;

    /// <summary>IDs of <see cref="TmbTrackEntry"/>.</summary>
    public readonly short[] TrackIds;

    internal TmbActorEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.AbilityDelay = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.TrackIds = this.ReadReferencedArray<short>(data, 12, 16);
    }
}

/// <summary>TMTR: a track, consisting of events.</summary>
public sealed class TmbTrackEntry : TmbIdentifiedEntry {
    /// <summary>IDs of <see cref="TmbEventEntry"/> (or other entries, such as TMFC).</summary>
    public readonly short[] EventIds;

    /// <summary>Offset of the Lua condition block, relative to <see cref="TmbEntry.BodyOffset"/>; 0 if none.</summary>
    public readonly uint LuaOffset;

    /// <summary>First value of the Lua condition block. Expected to be 8.</summary>
    public readonly uint LuaHeaderValue;

    /// <summary>Lua condition entries.</summary>
    public readonly TmbLuaEntry[] LuaEntries = [];

    internal TmbTrackEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.EventIds = this.ReadReferencedArray<short>(data, 4, 8);
        this.LuaOffset = this.ReadUInt32(12);
        if (this.LuaOffset == 0)
            return;

        var header = this.ReadArray<uint>(data, this.LuaOffset, 2);
        if (header.Length != 2)
            return;

        this.LuaHeaderValue = header[0];
        this.LuaEntries = this.ReadArray<TmbLuaEntry>(data, this.LuaOffset + 8, header[1]);
    }
}

/// <summary>An entry of the Lua condition block of a <see cref="TmbTrackEntry"/>.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TmbLuaEntry {
    public uint Value0;
    public uint Value1;
    public uint Value2;

    public override string ToString() => $"{this.Value0:X8} {this.Value1:X8} {this.Value2:X8}";
}