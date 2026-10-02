using System;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.AtchStructs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Attachment offset file (e.g. chara/xls/attachOffset/c0101.atch); one file per race,
/// containing for every attachment point (weapon, tool, etc.) one placement entry per state.
/// </summary>
/// <remarks>
/// Layout: u16 point count, u16 entry (state) count, u32 type per point,
/// 32-byte accessory bitfield (bit i = point i), then per point, per state, a 32-byte entry
/// (u32 absolute offset of null-terminated bone name, f32 scale, 3x f32 offset, 3x f32 rotation),
/// followed by the string pool.
/// </remarks>
[FileExtension(".atch")]
public class AtchFile : FileResource {
    public const int BitFieldSize = 32;

    public AtchPoint[] Points = [];

    /// <summary>Number of entries (states) per attachment point.</summary>
    public int EntryCount;

    public override void LoadFile()
    {
        var numPoints = this.Reader.ReadUInt16();
        this.EntryCount = this.Reader.ReadUInt16();

        this.Points = new AtchPoint[numPoints];
        for (var i = 0; i < numPoints; i++)
            this.Points[i] = new() {Type = (AtchType) this.Reader.ReadUInt32()};

        var bitfield = this.Reader.ReadBytes(BitFieldSize);
        for (var i = 0; i < numPoints && (i >> 3) < bitfield.Length; i++)
            this.Points[i].Accessory = ((bitfield[i >> 3] >> (i & 7)) & 1) != 0;

        foreach (var point in this.Points) {
            point.Entries = new AtchEntry[this.EntryCount];
            for (var i = 0; i < this.EntryCount; i++) {
                var stringOffset = this.Reader.ReadInt32();
                point.Entries[i] = new(
                    ReadCStringAt(this.Data, stringOffset),
                    this.Reader.ReadSingle(),
                    this.Reader.ReadSingleVector3(),
                    this.Reader.ReadSingleVector3());
            }
        }
    }

    public bool Valid =>
        this.Points.Length > 0 && this.Points.All(e => e.Type != 0 && e.Entries.Length == this.EntryCount);

    public AtchPoint? GetPoint(AtchType type) => this.Points.FirstOrDefault(p => p.Type == type);

    public AtchEntry GetEntry(AtchType type, int entryIndex)
    {
        if (this.GetPoint(type) is not { } point || entryIndex < 0 || point.Entries.Length <= entryIndex)
            throw new IndexOutOfRangeException();
        return point.Entries[entryIndex];
    }

    private static string ReadCStringAt(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset > data.Length)
            throw new IndexOutOfRangeException($"String offset 0x{offset:X} is out of range.");
        var span = data[offset..];
        var len = span.IndexOf((byte) 0);
        return Encoding.UTF8.GetString(len < 0 ? span : span[..len]);
    }
}
