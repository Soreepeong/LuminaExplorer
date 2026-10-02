using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Equipment parameter file (chara/xls/equipmentParameter/equipmentParameter.eqp).
/// Describes, per primary (set) ID, how a piece of equipment affects other parts of the character.
/// See <see cref="EqpGmpBlockTable"/> for the block layout.
/// </summary>
[FileExtension(".eqp")]
public class EqpFile : FileResource {
    public const int BlockSize = EqpGmpBlockTable.BlockSize;
    public const int NumBlocks = EqpGmpBlockTable.NumBlocks;
    public const int EntryCount = EqpGmpBlockTable.EntryCount;

    /// <summary>The entry used by the game for set IDs whose block is absent from the file.</summary>
    /// <remarks>cf. Client::Graphics::Scene::CharacterUtility.GetSlotEqpFlags</remarks>
    public const EqpFlags DefaultEntry = (EqpFlags) 0x3fe00070603f00ul;

    /// <summary>Bit N set means block N (set IDs N*160 to N*160+159) is stored in the file.</summary>
    public ulong ControlBlock;

    /// <summary>Entries indexed by set ID; absent blocks are filled with <see cref="DefaultEntry"/>.</summary>
    public EqpFlags[] Entries = null!;

    public override void LoadFile()
    {
        var expanded = EqpGmpBlockTable.Expand(this.Data.AsSpan(), (ulong) DefaultEntry, out this.ControlBlock);
        this.Entries = MemoryMarshal.Cast<ulong, EqpFlags>(expanded.AsSpan()).ToArray();
    }

    public EqpFlags this[int setId] => this.GetEntry(setId);

    /// <summary>Gets the entry for a set ID, or <see cref="DefaultEntry"/> if it is out of range.</summary>
    public EqpFlags GetEntry(int setId) =>
        setId is >= 0 and < EntryCount ? this.Entries[setId] : DefaultEntry;

    /// <summary>Gets the flags of an entry that belong to the given slot, still at their original bit positions.</summary>
    public EqpFlags GetEntry(int setId, EqpSlot slot) => this.GetEntry(setId) & GetSlotMask(slot);

    public bool IsBlockPresent(int blockIndex) => EqpGmpBlockTable.IsBlockPresent(this.ControlBlock, blockIndex);

    /// <summary>Whether the set ID is backed by data in the file, rather than the default entry.</summary>
    public bool IsSetIdPresent(int setId) => EqpGmpBlockTable.IsSetIdPresent(this.ControlBlock, setId);

    /// <summary>Enumerates all set IDs (starting from 1) that are backed by data in the file.</summary>
    public IEnumerable<(ushort SetId, EqpFlags Flags)> PresentEntries {
        get {
            for (var i = 1; i < EntryCount; i++) {
                if (this.IsSetIdPresent(i))
                    yield return ((ushort) i, this.Entries[i]);
            }
        }
    }

    public static EqpFlags GetSlotMask(EqpSlot slot) => slot switch {
        EqpSlot.Body => EqpFlags.BodyMask,
        EqpSlot.Legs => EqpFlags.LegsMask,
        EqpSlot.Hands => EqpFlags.HandsMask,
        EqpSlot.Feet => EqpFlags.FeetMask,
        EqpSlot.Head => EqpFlags.HeadMask,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    /// <summary>Gets the bit offset of the first flag for the given slot.</summary>
    public static int GetSlotBitOffset(EqpSlot slot) => slot switch {
        EqpSlot.Body => 0,
        EqpSlot.Legs => 16,
        EqpSlot.Hands => 24,
        EqpSlot.Feet => 32,
        EqpSlot.Head => 40,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    public enum EqpSlot {
        Body,
        Legs,
        Hands,
        Feet,
        Head,
    }

    [Flags]
    public enum EqpFlags : ulong {
        BodyEnabled = 0x00_01ul,
        BodyHideWaist = 0x00_02ul,
        BodyHideThighs = 0x00_04ul,
        BodyHideGlovesS = 0x00_08ul,
        BodyHideGloveCuffs = 0x00_10ul,
        BodyHideGlovesM = 0x00_20ul,
        BodyHideGlovesL = 0x00_40ul,
        BodyHideGorget = 0x00_80ul,
        BodyShowLeg = 0x01_00ul,
        BodyShowHand = 0x02_00ul,
        BodyShowHead = 0x04_00ul,
        BodyShowNecklace = 0x08_00ul,
        BodyShowBracelet = 0x10_00ul,
        BodyShowTail = 0x20_00ul,
        BodyDisableBreastPhysics = 0x40_00ul,
        BodyUsesEvpTable = 0x80_00ul,
        BodyMask = 0xFF_FFul,

        LegsEnabled = 0x01ul << 16,
        LegsHideKneePads = 0x02ul << 16,
        LegsHideBootsS = 0x04ul << 16,
        LegsHideBootsM = 0x08ul << 16,
        LegsUnknown20 = 0x10ul << 16,
        LegsShowFoot = 0x20ul << 16,
        LegsShowTail = 0x40ul << 16,
        LegsUnknown23 = 0x80ul << 16,
        LegsMask = 0xFFul << 16,

        HandsEnabled = 0x01ul << 24,
        HandsHideElbow = 0x02ul << 24,
        HandsHideForearm = 0x04ul << 24,
        HandsUnknown27 = 0x08ul << 24,
        HandsShowBracelet = 0x10ul << 24,
        HandsShowRingL = 0x20ul << 24,
        HandsShowRingR = 0x40ul << 24,
        HandsUnknown31 = 0x80ul << 24,
        HandsMask = 0xFFul << 24,

        FeetEnabled = 0x01ul << 32,
        FeetHideKnee = 0x02ul << 32,
        FeetHideCalf = 0x04ul << 32,
        FeetHideAnkle = 0x08ul << 32,
        FeetUnknown36 = 0x10ul << 32,
        FeetUnknown37 = 0x20ul << 32,
        FeetUnknown38 = 0x40ul << 32,
        FeetUnknown39 = 0x80ul << 32,
        FeetMask = 0xFFul << 32,

        HeadEnabled = 0x00_00_01ul << 40,
        HeadHideScalp = 0x00_00_02ul << 40,
        HeadHideHair = 0x00_00_04ul << 40,
        HeadShowHairOverride = 0x00_00_08ul << 40,
        HeadHideNeck = 0x00_00_10ul << 40,
        HeadShowNecklace = 0x00_00_20ul << 40,
        HeadShowEarringsHyurRoe = 0x00_00_40ul << 40,
        HeadShowEarringsLalaElezen = 0x00_00_80ul << 40,
        HeadShowEarringsMiqoHrothViera = 0x00_01_00ul << 40,
        HeadShowEarringsAuRa = 0x00_02_00ul << 40,
        HeadShowEarHuman = 0x00_04_00ul << 40,
        HeadShowEarMiqote = 0x00_08_00ul << 40,
        HeadShowEarAuRa = 0x00_10_00ul << 40,
        HeadShowEarViera = 0x00_20_00ul << 40,
        HeadDisableBangsPhysics = 0x00_40_00ul << 40,
        HeadDisableHairPhysics = 0x00_80_00ul << 40,
        HeadShowHrothgarHat = 0x01_00_00ul << 40,
        HeadShowVieraHat = 0x02_00_00ul << 40,
        HeadUsesEvpTable = 0x04_00_00ul << 40,
        HeadUnknown59 = 0x08_00_00ul << 40,
        HeadUnknown60 = 0x10_00_00ul << 40,
        HeadUnknown61 = 0x20_00_00ul << 40,
        HeadUnknown62 = 0x40_00_00ul << 40,
        HeadUnknown63 = 0x80_00_00ul << 40,
        HeadMask = 0xFF_FF_FFul << 40,
    }
}
