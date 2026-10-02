using System;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Character make parameter file (chara/xls/charamake/human.cmp).
/// A headerless blob of color tables followed by the racial scaling table; only the latter is parsed here.
/// <code>
/// 0x00000  2 x ColorParameters (0x2400 bytes each; general and interface)
/// 0x04800  32 x GenderClanColorParameters (0x1400 bytes each)
/// 0x2C800  8 x 10 x RacialScale (56 bytes each)    row = (clan - 1) / 2, column = (clan - 1) % 2
/// 0x2D980  end of file
/// </code>
/// </summary>
[FileExtension(".cmp")]
public class CmpFile : FileResource {
    public const int RacialScalingOffset = 0x2C800;
    public const int ScaleRowCount = 8;
    public const int ScalesPerRow = 10;
    public const int ScaleCount = ScaleRowCount * ScalesPerRow;
    public const int ScaleEntrySize = 56;
    public const int ExpectedFileSize = RacialScalingOffset + ScaleCount * ScaleEntrySize;

    /// <summary>All 80 scale entries in file order; index = row * <see cref="ScalesPerRow"/> + column.</summary>
    /// <remarks>Only columns 0 and 1 of each row are used by clans.</remarks>
    public RacialScale[] Scales = null!;

    public override void LoadFile()
    {
        if (this.Data.Length < ExpectedFileSize)
            throw new InvalidDataException("File is too small to contain the racial scaling table.");

        this.Scales = MemoryMarshal.Cast<byte, RacialScale>(
            this.Data.AsSpan(RacialScalingOffset, ScaleCount * ScaleEntrySize)).ToArray();
    }

    /// <summary>Gets the index into <see cref="Scales"/> for a clan.</summary>
    public static int GetScaleIndex(Clan clan)
    {
        if (clan is <= Clan.Unknown or > Clan.Veena)
            throw new ArgumentOutOfRangeException(nameof(clan), clan, null);
        var i = (int) clan - 1;
        return (i >> 1) * ScalesPerRow + (i & 1);
    }

    public RacialScale GetScale(Clan clan) => this.Scales[GetScaleIndex(clan)];

    public float this[Clan clan, RspAttribute attribute] => this.GetScale(clan)[attribute];

    /// <summary>Clans (sub-races), in the game's numbering.</summary>
    public enum Clan : byte {
        Unknown,
        Midlander,
        Highlander,
        Wildwood,
        Duskwight,
        Plainsfolk,
        Dunesfolk,
        SeekerOfTheSun,
        KeeperOfTheMoon,
        Seawolf,
        Hellsguard,
        Raen,
        Xaela,
        Helion,
        Lost,
        Rava,
        Veena,
    }

    /// <summary>Racial scaling parameters, in the order they are stored in a <see cref="RacialScale"/>.</summary>
    public enum RspAttribute : byte {
        MaleMinSize,
        MaleMaxSize,
        MaleMinTail,
        MaleMaxTail,
        FemaleMinSize,
        FemaleMaxSize,
        FemaleMinTail,
        FemaleMaxTail,
        BustMinX,
        BustMinY,
        BustMinZ,
        BustMaxX,
        BustMaxY,
        BustMaxZ,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MinMax {
        public float Minimum;
        public float Maximum;

        public override string ToString() => $"{this.Minimum} ~ {this.Maximum}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MinMax3 {
        public float MinimumX;
        public float MinimumY;
        public float MinimumZ;
        public float MaximumX;
        public float MaximumY;
        public float MaximumZ;

        public override string ToString() =>
            $"({this.MinimumX}, {this.MinimumY}, {this.MinimumZ}) ~ ({this.MaximumX}, {this.MaximumY}, {this.MaximumZ})";
    }

    [StructLayout(LayoutKind.Sequential, Size = ScaleEntrySize)]
    public struct RacialScale {
        public MinMax MaleHeight;
        public MinMax MaleTail;
        public MinMax FemaleHeight;
        public MinMax FemaleTail;
        public MinMax3 BreastSize;

        public readonly float this[RspAttribute attribute] => attribute switch {
            RspAttribute.MaleMinSize => this.MaleHeight.Minimum,
            RspAttribute.MaleMaxSize => this.MaleHeight.Maximum,
            RspAttribute.MaleMinTail => this.MaleTail.Minimum,
            RspAttribute.MaleMaxTail => this.MaleTail.Maximum,
            RspAttribute.FemaleMinSize => this.FemaleHeight.Minimum,
            RspAttribute.FemaleMaxSize => this.FemaleHeight.Maximum,
            RspAttribute.FemaleMinTail => this.FemaleTail.Minimum,
            RspAttribute.FemaleMaxTail => this.FemaleTail.Maximum,
            RspAttribute.BustMinX => this.BreastSize.MinimumX,
            RspAttribute.BustMinY => this.BreastSize.MinimumY,
            RspAttribute.BustMinZ => this.BreastSize.MinimumZ,
            RspAttribute.BustMaxX => this.BreastSize.MaximumX,
            RspAttribute.BustMaxY => this.BreastSize.MaximumY,
            RspAttribute.BustMaxZ => this.BreastSize.MaximumZ,
            _ => throw new ArgumentOutOfRangeException(nameof(attribute), attribute, null),
        };
    }
}
