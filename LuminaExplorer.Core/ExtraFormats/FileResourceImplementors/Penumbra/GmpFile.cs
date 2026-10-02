using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Gimmick parameter file (chara/xls/equipmentParameter/gimmickParameter.gmp).
/// Describes, per head equipment primary (set) ID, the visor behavior.
/// See <see cref="EqpGmpBlockTable"/> for the block layout.
/// </summary>
[FileExtension(".gmp")]
public class GmpFile : FileResource {
    public const int BlockSize = EqpGmpBlockTable.BlockSize;
    public const int NumBlocks = EqpGmpBlockTable.NumBlocks;
    public const int EntryCount = EqpGmpBlockTable.EntryCount;

    /// <summary>Bit N set means block N (set IDs N*160 to N*160+159) is stored in the file.</summary>
    public ulong ControlBlock;

    /// <summary>Entries indexed by set ID; absent blocks are filled with empty (all-zero) entries.</summary>
    public GmpEntry[] Entries = null!;

    public override void LoadFile()
    {
        var expanded = EqpGmpBlockTable.Expand(this.Data.AsSpan(), 0, out this.ControlBlock);
        this.Entries = expanded.Select(x => new GmpEntry(x)).ToArray();
    }

    public GmpEntry this[int setId] => this.GetEntry(setId);

    /// <summary>Gets the entry for a set ID, or an empty entry if it is out of range.</summary>
    public GmpEntry GetEntry(int setId) =>
        setId is >= 0 and < EntryCount ? this.Entries[setId] : default;

    public bool IsBlockPresent(int blockIndex) => EqpGmpBlockTable.IsBlockPresent(this.ControlBlock, blockIndex);

    /// <summary>Whether the set ID is backed by data in the file, rather than an empty entry.</summary>
    public bool IsSetIdPresent(int setId) => EqpGmpBlockTable.IsSetIdPresent(this.ControlBlock, setId);

    /// <summary>Enumerates all set IDs (starting from 1) that are backed by data in the file.</summary>
    public IEnumerable<(ushort SetId, GmpEntry Entry)> PresentEntries {
        get {
            for (var i = 1; i < EntryCount; i++) {
                if (this.IsSetIdPresent(i))
                    yield return ((ushort) i, this.Entries[i]);
            }
        }
    }

    /// <summary>
    /// A gimmick parameter entry. Stored as 8 bytes, of which only the low 5 bytes (40 bits) are used.
    /// </summary>
    public readonly struct GmpEntry : IEquatable<GmpEntry> {
        public readonly ulong Value;

        public GmpEntry(ulong value)
        {
            this.Value = value;
        }

        /// <summary>Whether the visor is enabled at all. Bit 0.</summary>
        public bool Enabled => (this.Value & 1) != 0;

        /// <summary>Whether toggling the visor is animated. Bit 1.</summary>
        public bool Animated => (this.Value & 2) != 0;

        /// <summary>Rotation A in degrees for a toggled visor. Bits 2-11.</summary>
        public ushort RotationA => (ushort) ((this.Value >> 2) & 0x3FF);

        /// <summary>Rotation B in degrees for a toggled visor. Bits 12-21.</summary>
        public ushort RotationB => (ushort) ((this.Value >> 12) & 0x3FF);

        /// <summary>Rotation C in degrees for a toggled visor. Bits 22-31.</summary>
        public ushort RotationC => (ushort) ((this.Value >> 22) & 0x3FF);

        /// <summary>Unknown parameter A. Bits 32-35.</summary>
        public byte UnknownA => (byte) ((this.Value >> 32) & 0xF);

        /// <summary>Unknown parameter B. Bits 36-39.</summary>
        public byte UnknownB => (byte) ((this.Value >> 36) & 0xF);

        /// <summary>Both unknown parameters together (byte 4).</summary>
        public byte UnknownTotal => (byte) (this.Value >> 32);

        public bool Equals(GmpEntry other) => this.Value == other.Value;

        public override bool Equals(object? obj) => obj is GmpEntry other && this.Equals(other);

        public override int GetHashCode() => this.Value.GetHashCode();

        public static bool operator ==(GmpEntry left, GmpEntry right) => left.Equals(right);

        public static bool operator !=(GmpEntry left, GmpEntry right) => !left.Equals(right);

        public override string ToString() =>
            $"{(this.Enabled ? "Enabled" : "Disabled")}{(this.Animated ? ", Animated" : "")}, " +
            $"Rotation ({this.RotationA}, {this.RotationB}, {this.RotationC}), " +
            $"Unknown ({this.UnknownA}, {this.UnknownB})";
    }
}
