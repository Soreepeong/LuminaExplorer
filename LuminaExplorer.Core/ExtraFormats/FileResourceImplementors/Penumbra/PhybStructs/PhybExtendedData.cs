using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.PhybStructs;

/// <summary>Extended data appended at the end of some physics files (Dawntrail).</summary>
/// <remarks>
/// Layout: [padding to 8] "EPHB" preamble (u32 magic, u16 version, u16 count, u64 table length), table bytes,
/// "PACK" postamble (u32 magic, u16 version, u16 count, u64 offset start, u64 total size) as the last 0x18 bytes.
/// </remarks>
public class PhybExtendedData {
    public const uint MagicPost = 0x4B434150u; // "PACK"
    public const uint MagicPre = 0x42485045u; // "EPHB"
    public const int PostambleSize = 0x18;
    public const int PreambleSize = 0x10;

    public uint PreambleMagic;
    public ushort PreambleVersion;
    public ushort PreambleCount;

    public ushort PostambleVersion;
    public ushort PostambleCount;
    public ulong PostambleOffsetStart;
    public ulong PostambleTotalSize;

    /// <summary>Offset of the preamble in the file; also the end of the simulator section.</summary>
    public long Offset;

    public byte[] Table = [];

    /// <summary>Tries to locate and read the extended data.</summary>
    /// <param name="data">Whole file data.</param>
    /// <param name="extendedOffset">Offset of the extended data, or the file length if there is none.</param>
    /// <returns>The extended data, or null if there is none.</returns>
    public static PhybExtendedData? Read(ReadOnlySpan<byte> data, out long extendedOffset)
    {
        extendedOffset = data.Length;
        if (data.Length < PostambleSize)
            return null;

        var post = data[^PostambleSize..];
        if (BitConverter.ToUInt32(post) != MagicPost)
            return null;

        var ret = new PhybExtendedData {
            PostambleVersion = BitConverter.ToUInt16(post[4..]),
            PostambleCount = BitConverter.ToUInt16(post[6..]),
            PostambleOffsetStart = BitConverter.ToUInt64(post[8..]),
            PostambleTotalSize = BitConverter.ToUInt64(post[16..]),
        };

        if (ret.PostambleTotalSize > (ulong) data.Length)
            return null;

        var pos = data.Length - (long) ret.PostambleTotalSize;

        // Same as Penumbra (taken from VFXEdit): if the preamble magic is not at the computed position,
        // the preamble is assumed to start 4 bytes later (past a 4-byte padding).
        if (BitConverter.ToUInt32(data[(int) pos..]) != MagicPre)
            pos += 4;

        if (pos + PreambleSize > data.Length)
            return null;

        var pre = data[(int) pos..];
        ret.Offset = extendedOffset = pos;
        ret.PreambleMagic = BitConverter.ToUInt32(pre);
        ret.PreambleVersion = BitConverter.ToUInt16(pre[4..]);
        ret.PreambleCount = BitConverter.ToUInt16(pre[6..]);
        var tableLength = BitConverter.ToUInt64(pre[8..]);
        var tableStart = (int) pos + PreambleSize;
        if (tableLength > (ulong) (data.Length - tableStart))
            throw new InvalidOperationException("Phyb extended data table exceeds the file.");
        ret.Table = data.Slice(tableStart, (int) tableLength).ToArray();
        return ret;
    }
}
