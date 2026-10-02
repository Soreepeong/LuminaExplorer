using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>Staining (dye) template file.</summary>
/// <remarks>
/// Layout: u16 magic ("MS"), u16 version, u16 entry count, u8 color column count, u8 scalar column count,
/// u32 keys[count], u32 offsets[count] (in units of u16, relative to the end of the offset array), entries.
/// Version 0x0101 is the legacy (pre-Dawntrail) layout with 3 colors and 2 scalars implied (counts in header are 0);
/// versions 0x0200 and 0x0201 specify the counts in the header (Dawntrail: 3 colors and 9 scalars).
/// Each entry has (colors + scalars) u16 end offsets (in units of u16), followed by the column data. A column has
/// either 0 elements (all default), 1 element (repeated for all stains), 254 elements (1 per stain), or N elements
/// followed by 254 bytes of 1-based indices (the first byte being an unused 0xFF marker).
/// </remarks>
[FileExtension(".stm")]
public class StmFile : FileResource {
    public const ushort MagicValue = 0x534D;
    public const string LegacyPath = "chara/base_material/stainingtemplate.stm";
    public const string GudPath = "chara/base_material/stainingtemplate_gud.stm";

    /// <summary>The number of stains is capped at 254 at the moment.</summary>
    public const int NumStains = 254;

    public ushort Magic;
    public ushort Version;
    public int ColorCount;
    public int ScalarCount;

    /// <summary>Template IDs, in file order.</summary>
    public uint[] Keys = [];

    /// <summary>All dyeing templates, by template ID.</summary>
    public readonly Dictionary<uint, StainingTemplateEntry> Entries = new();

    public StmLayout Layout => (this.ColorCount, this.ScalarCount) switch {
        (LegacyDyePack.NumColors, LegacyDyePack.NumScalars) => StmLayout.Legacy,
        (DyePack.NumColors, DyePack.NumScalars) => StmLayout.Dawntrail,
        _ => StmLayout.Unknown,
    };

    public override void LoadFile()
    {
        this.Magic = this.Reader.ReadUInt16();
        if (this.Magic != MagicValue)
            throw new InvalidDataException($"Invalid STM magic number 0x{this.Magic:X4}");

        this.Version = this.Reader.ReadUInt16();
        var numEntries = this.Reader.ReadUInt16();
        int numColors = this.Reader.ReadByte();
        int numScalars = this.Reader.ReadByte();

        switch (this.Version) {
            case 0x0101:
                if (numColors is not 0 || numScalars is not 0)
                    throw new InvalidDataException(
                        $"Unexpected column counts in STM v1.1 file: {numColors} colors, {numScalars} scalars");

                numColors = LegacyDyePack.NumColors;
                numScalars = LegacyDyePack.NumScalars;
                break;
            case 0x0200:
            case 0x0201:
                break;
            default:
                throw new InvalidDataException(
                    $"Unrecognized STM version v{this.Version >> 8}.{this.Version & 0xFF}");
        }

        this.ColorCount = numColors;
        this.ScalarCount = numScalars;

        this.Keys = this.Reader.ReadUInt32Array(numEntries);
        var offsets = this.Reader.ReadUInt32Array(numEntries);
        var dataStart = this.Reader.BaseStream.Position;
        var dataLength = this.Reader.BaseStream.Length - dataStart;

        for (var i = 0; i < numEntries; i++) {
            var start = (long) offsets[i] << 1;
            var end = i + 1 < numEntries ? (long) offsets[i + 1] << 1 : dataLength;
            if (start > end || end > dataLength)
                throw new InvalidDataException($"STM entry {i} has an invalid range [{start}, {end}).");

            var bytes = this.Data.AsSpan((int) (dataStart + start), (int) (end - start));
            this.Entries.Add(this.Keys[i], new(bytes, numColors, numScalars));
        }
    }

    /// <summary>Tries to get a legacy (pre-Dawntrail) dye pack.</summary>
    /// <param name="template">Template ID.</param>
    /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>.</param>
    /// <param name="dyes">Retrieved dye pack.</param>
    /// <returns>True on success.</returns>
    public bool TryGetLegacyDyePack(uint template, int stainId, out LegacyDyePack dyes)
    {
        if (this.Layout == StmLayout.Legacy
            && stainId is > 0 and <= NumStains
            && this.Entries.TryGetValue(template, out var entry)) {
            dyes = entry.GetLegacyDyePack(stainId);
            return true;
        }

        dyes = default;
        return false;
    }

    /// <summary>Tries to get a Dawntrail dye pack.</summary>
    /// <param name="template">Template ID.</param>
    /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>.</param>
    /// <param name="dyes">Retrieved dye pack.</param>
    /// <returns>True on success.</returns>
    public bool TryGetDyePack(uint template, int stainId, out DyePack dyes)
    {
        if (this.Layout == StmLayout.Dawntrail
            && stainId is > 0 and <= NumStains
            && this.Entries.TryGetValue(template, out var entry)) {
            dyes = entry.GetDyePack(stainId);
            return true;
        }

        dyes = default;
        return false;
    }

    public enum StmLayout {
        Unknown,
        Legacy,
        Dawntrail,
    }

    /// <summary>How the values of a column are stored in the file.</summary>
    public enum ColumnStorage {
        /// <summary>No values; all stains use the default (zero) value.</summary>
        AllDefault,

        /// <summary>A single value used for all stains.</summary>
        Single,

        /// <summary>One value per stain.</summary>
        Full,

        /// <summary>A small set of values, referenced per stain by 1-based index (0 = default).</summary>
        Indexed,
    }

    /// <summary>Color made of three half-precision floats.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public record struct HalfColor(Half Red, Half Green, Half Blue) {
        public Vector3 ToVector3() => new((float) this.Red, (float) this.Green, (float) this.Blue);

        public override string ToString() => $"({this.Red}, {this.Green}, {this.Blue})";
    }

    /// <summary>All dye-able color set information for a row - legacy format.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public record struct LegacyDyePack {
        public const int NumColors = 3;
        public const int NumScalars = 2;

        public HalfColor DiffuseColor;
        public HalfColor SpecularColor;
        public HalfColor EmissiveColor;
        public Half Shininess;
        public Half SpecularMask;
    }

    /// <summary>All dye-able color set information for a row - GUD (Dawntrail) format.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public record struct DyePack {
        public const int NumColors = 3;
        public const int NumScalars = 9;

        public HalfColor DiffuseColor;
        public HalfColor SpecularColor;
        public HalfColor EmissiveColor;
        public Half Exposure;
        public Half Metalness;
        public Half Roughness;
        public Half SheenRate;
        public Half SheenTintRate;
        public Half SheenAperture;
        public Half Anisotropy;
        public Half RawSphereMapIndex;
        public Half SphereMapMask;

        public readonly ushort SphereMapIndex => (ushort) this.RawSphereMapIndex;
    }

    /// <summary>A single dyeing template.</summary>
    public sealed class StainingTemplateEntry {
        /// <summary>Per color column, the value for every stain (index = stain ID - 1).</summary>
        public readonly HalfColor[][] Colors;

        /// <summary>Per scalar column, the value for every stain (index = stain ID - 1).</summary>
        public readonly Half[][] Scalars;

        /// <summary>Storage kind of each color column.</summary>
        public readonly ColumnStorage[] ColorStorages;

        /// <summary>Storage kind of each scalar column.</summary>
        public readonly ColumnStorage[] ScalarStorages;

        public StainingTemplateEntry(ReadOnlySpan<byte> data, int numColors, int numScalars)
        {
            var numColumns = numColors + numScalars;
            var headerSize = numColumns * 2;
            if (data.Length < headerSize)
                throw new InvalidDataException("STM entry is too short.");

            var byteCounts = new int[numColumns];
            var lastEnd = 0;
            for (var i = 0; i < numColumns; i++) {
                var nextEnd = BitConverter.ToUInt16(data[(i * 2)..]) * 2; // because the ends are in terms of ushort.
                byteCounts[i] = nextEnd - lastEnd;
                lastEnd = nextEnd;
            }

            this.Colors = new HalfColor[numColors][];
            this.Scalars = new Half[numScalars][];
            this.ColorStorages = new ColumnStorage[numColors];
            this.ScalarStorages = new ColumnStorage[numScalars];

            var pos = headerSize;
            var j = 0;
            for (var i = 0; i < numColors; i++, j++) {
                this.Colors[i] = ReadColumn<HalfColor>(Slice(data, ref pos, byteCounts[j]), out this.ColorStorages[i]);
            }

            for (var i = 0; i < numScalars; i++, j++) {
                this.Scalars[i] = ReadColumn<Half>(Slice(data, ref pos, byteCounts[j]), out this.ScalarStorages[i]);
            }
        }

        /// <summary>Gets the color of a column for a stain.</summary>
        /// <param name="column">Color column index.</param>
        /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>.</param>
        /// <returns>The value, or default if out of range.</returns>
        public HalfColor GetColor(int column, int stainId) =>
            stainId is > 0 and <= NumStains ? this.Colors[column][stainId - 1] : default;

        /// <summary>Gets the scalar of a column for a stain.</summary>
        /// <param name="column">Scalar column index.</param>
        /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>.</param>
        /// <returns>The value, or default if out of range.</returns>
        public Half GetScalar(int column, int stainId) =>
            stainId is > 0 and <= NumStains ? this.Scalars[column][stainId - 1] : default;

        /// <summary>Gets the legacy dye pack of a stain. Requires an entry with the legacy layout.</summary>
        /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>. 0th index is skipped.</param>
        public LegacyDyePack GetLegacyDyePack(int stainId)
        {
            var pack = new LegacyDyePack();
            this.FillPack(MemoryMarshal.Cast<LegacyDyePack, Half>(new Span<LegacyDyePack>(ref pack)), stainId);
            return pack;
        }

        /// <summary>Gets the Dawntrail dye pack of a stain. Requires an entry with the Dawntrail layout.</summary>
        /// <param name="stainId">Stain ID, from 1 to <see cref="NumStains"/>. 0th index is skipped.</param>
        public DyePack GetDyePack(int stainId)
        {
            var pack = new DyePack();
            this.FillPack(MemoryMarshal.Cast<DyePack, Half>(new Span<DyePack>(ref pack)), stainId);
            return pack;
        }

        private void FillPack(Span<Half> packSpan, int stainId)
        {
            if (stainId is <= 0 or > NumStains)
                return;
            if (packSpan.Length != this.Colors.Length * 3 + this.Scalars.Length)
                throw new InvalidOperationException("Dye pack type does not match the STM layout.");

            var idx = stainId - 1;
            var colors = MemoryMarshal.Cast<Half, HalfColor>(packSpan[..(this.Colors.Length * 3)]);
            var scalars = packSpan[(this.Colors.Length * 3)..];
            for (var i = 0; i < colors.Length; i++)
                colors[i] = this.Colors[i][idx];
            for (var i = 0; i < scalars.Length; i++)
                scalars[i] = this.Scalars[i][idx];
        }

        private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, ref int pos, int length)
        {
            if (length < 0 || pos + length > data.Length)
                throw new InvalidDataException("STM entry column exceeds the entry.");
            var ret = data.Slice(pos, length);
            pos += length;
            return ret;
        }

        private static unsafe T[] ReadColumn<T>(ReadOnlySpan<byte> data, out ColumnStorage storage)
            where T : unmanaged
        {
            var arraySize = data.Length / sizeof(T);
            var ret = new T[NumStains];
            switch (arraySize) {
                case 0:
                    // All default
                    storage = ColumnStorage.AllDefault;
                    break;
                case 1:
                    // All single entry
                    storage = ColumnStorage.Single;
                    Array.Fill(ret, MemoryMarshal.Read<T>(data));
                    break;
                case NumStains:
                    // 1-to-1 entries
                    storage = ColumnStorage.Full;
                    MemoryMarshal.Cast<byte, T>(data[..(NumStains * sizeof(T))]).CopyTo(ret);
                    break;
                case < NumStains: {
                    // Indexed access: N values, followed by NumStains bytes of indices.
                    storage = ColumnStorage.Indexed;
                    if (data.Length < NumStains)
                        throw new InvalidDataException("STM indexed column is too short.");

                    var count = (data.Length - NumStains) / sizeof(T);
                    var values = new T[count + 1];
                    MemoryMarshal.Cast<byte, T>(data[..(count * sizeof(T))]).CopyTo(values.AsSpan(1));

                    // First byte seems to be an unused 0xFF byte marker. Necessary for correct offsets.
                    // (As in Penumbra, this leaves the last stain using index 0, i.e. the default value.)
                    var indices = data.Slice(count * sizeof(T), NumStains)[1..];
                    for (var i = 0; i < indices.Length; i++)
                        ret[i] = values[indices[i] > count ? 0 : indices[i]];
                    break;
                }
                default:
                    throw new InvalidDataException($"Stain Template can not have more than {NumStains} elements.");
            }

            return ret;
        }
    }
}
