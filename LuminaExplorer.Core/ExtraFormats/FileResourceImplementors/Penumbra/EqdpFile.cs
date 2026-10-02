using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Equipment/accessory deformer parameter file
/// (chara/xls/charadb/equipmentDeformerParameter/cXXXX.eqdp, chara/xls/charadb/accessoryDeformerParameter/cXXXX.eqdp).
/// Each file belongs to one gender-race code (the XXXX in the file name), and describes, per primary (set) ID,
/// for which slots that gender-race has its own material and model.
/// <code>
/// [Identifier:ushort][BlockSize:ushort][BlockCount:ushort]
/// BlockCount x [BlockOffset:ushort]      in entries, relative to the end of this array; 0xFFFF = absent block
/// N x [Entry:ushort]
/// </code>
/// </summary>
[FileExtension(".eqdp")]
public class EqdpFile : FileResource {
    public const ushort CollapsedBlock = ushort.MaxValue;
    public const int HeaderSize = 6;

    public ushort Identifier;
    public ushort BlockSize;
    public ushort BlockCount;

    /// <summary>Offset of each block, in entries, into <see cref="BlockData"/>; <see cref="CollapsedBlock"/> if absent.</summary>
    public ushort[] BlockOffsets = null!;

    /// <summary>All entries following the header (includes any trailing padding of the file).</summary>
    public EqdpFlags[] BlockData = null!;

    public override void LoadFile()
    {
        this.Identifier = this.Reader.ReadUInt16();
        this.BlockSize = this.Reader.ReadUInt16();
        this.BlockCount = this.Reader.ReadUInt16();

        var dataOffset = HeaderSize + this.BlockCount * 2;
        if (this.Data.Length < dataOffset)
            throw new InvalidDataException("File is too small to contain the block offsets.");

        var span = this.Data.AsSpan();
        this.BlockOffsets = MemoryMarshal.Cast<byte, ushort>(span[HeaderSize..dataOffset]).ToArray();

        var data = span[dataOffset..];
        this.BlockData = MemoryMarshal.Cast<byte, EqdpFlags>(data[..(data.Length / 2 * 2)]).ToArray();
    }

    /// <summary>Number of addressable set IDs.</summary>
    public int Count => this.BlockSize * this.BlockCount;

    public EqdpFlags this[int setId] => this.GetEntry(setId);

    public bool IsBlockPresent(int blockIndex) =>
        blockIndex >= 0 && blockIndex < this.BlockCount && this.BlockOffsets[blockIndex] != CollapsedBlock;

    /// <summary>Gets the entry for a set ID; returns false if the set ID falls into an absent block.</summary>
    public bool TryGetEntry(int setId, out EqdpFlags entry)
    {
        entry = EqdpFlags.None;
        if (setId < 0 || this.BlockSize == 0)
            return false;

        var blockIndex = setId / this.BlockSize;
        if (!this.IsBlockPresent(blockIndex))
            return false;

        var index = this.BlockOffsets[blockIndex] + setId % this.BlockSize;
        if (index >= this.BlockData.Length)
            return false;

        entry = this.BlockData[index];
        return true;
    }

    /// <summary>Gets the entry for a set ID, or <see cref="EqdpFlags.None"/> if absent.</summary>
    public EqdpFlags GetEntry(int setId) => this.TryGetEntry(setId, out var entry) ? entry : EqdpFlags.None;

    /// <summary>Gets the (material, model) bits of the given slot for a set ID.</summary>
    public (bool Material, bool Model) GetEntry(int setId, EqdpSlot slot) => GetSlotBits(this.GetEntry(setId), slot);

    /// <summary>Enumerates all set IDs that are backed by data in the file.</summary>
    public IEnumerable<(ushort SetId, EqdpFlags Flags)> PresentEntries {
        get {
            for (var block = 0; block < this.BlockCount; block++) {
                if (!this.IsBlockPresent(block))
                    continue;
                for (var i = 0; i < this.BlockSize; i++) {
                    var setId = block * this.BlockSize + i;
                    if (this.TryGetEntry(setId, out var entry))
                        yield return ((ushort) setId, entry);
                }
            }
        }
    }

    /// <summary>Gets the bit offset of the slot's (material, model) bit pair.</summary>
    /// <remarks>Equipment files use Head..Feet, accessory files use Ears..RingL; both share the same bit positions.</remarks>
    public static int GetSlotBitOffset(EqdpSlot slot) => slot switch {
        EqdpSlot.Head or EqdpSlot.Ears => 0,
        EqdpSlot.Body or EqdpSlot.Neck => 2,
        EqdpSlot.Hands or EqdpSlot.Wrists => 4,
        EqdpSlot.Legs or EqdpSlot.RingR => 6,
        EqdpSlot.Feet or EqdpSlot.RingL => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    public static EqdpFlags GetSlotMask(EqdpSlot slot) => (EqdpFlags) (3 << GetSlotBitOffset(slot));

    /// <summary>Gets the (material, model) bits of the given slot.</summary>
    public static (bool Material, bool Model) GetSlotBits(EqdpFlags entry, EqdpSlot slot)
    {
        var offset = GetSlotBitOffset(slot);
        return ((((int) entry >> offset) & 1) != 0, (((int) entry >> (offset + 1)) & 1) != 0);
    }

    /// <summary>Tries to extract the gender-race code (e.g. 101 for c0101) from an .eqdp file path.</summary>
    public static bool TryParseGenderRaceCode(string path, out ushort genderRaceCode)
    {
        genderRaceCode = 0;
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length == 5
            && name[0] is 'c' or 'C'
            && ushort.TryParse(name.AsSpan(1), out genderRaceCode);
    }

    public enum EqdpSlot {
        Head,
        Body,
        Hands,
        Legs,
        Feet,
        Ears,
        Neck,
        Wrists,
        RingR,
        RingL,
    }

    /// <summary>
    /// Each slot has two bits: the lower one indicates a race-specific material, the higher one a race-specific model.
    /// Equipment and accessory files share bit positions, so the accessory names alias the equipment names.
    /// </summary>
    [Flags]
    public enum EqdpFlags : ushort {
        None = 0,

        HeadMaterial = 1 << 0,
        HeadModel = 1 << 1,
        HeadMask = HeadMaterial | HeadModel,
        BodyMaterial = 1 << 2,
        BodyModel = 1 << 3,
        BodyMask = BodyMaterial | BodyModel,
        HandsMaterial = 1 << 4,
        HandsModel = 1 << 5,
        HandsMask = HandsMaterial | HandsModel,
        LegsMaterial = 1 << 6,
        LegsModel = 1 << 7,
        LegsMask = LegsMaterial | LegsModel,
        FeetMaterial = 1 << 8,
        FeetModel = 1 << 9,
        FeetMask = FeetMaterial | FeetModel,

        EarsMaterial = HeadMaterial,
        EarsModel = HeadModel,
        EarsMask = HeadMask,
        NeckMaterial = BodyMaterial,
        NeckModel = BodyModel,
        NeckMask = BodyMask,
        WristsMaterial = HandsMaterial,
        WristsModel = HandsModel,
        WristsMask = HandsMask,
        RingRMaterial = LegsMaterial,
        RingRModel = LegsModel,
        RingRMask = LegsMask,
        RingLMaterial = FeetMaterial,
        RingLModel = FeetModel,
        RingLMask = FeetMask,

        FullMask = 0x3FF,
    }
}
