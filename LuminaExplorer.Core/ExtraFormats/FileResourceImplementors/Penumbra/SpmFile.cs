using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>Shader parameter table file (common/graphics/*_shader_param.spm).</summary>
/// <remarks>
/// Layout: <see cref="SpmHeader"/> (12 bytes; section offsets are in units of 4 bytes),
/// column definitions (u32 name CRC, u32 type), row definitions (u32 table CRC, u32 index),
/// and values (u32 each, row-major: rows x columns).
/// </remarks>
[FileExtension(".spm")]
public class SpmFile : FileResource {
    public const uint SupportedVersion = 0x01000000u;

    public SpmHeader Header;

    public SpmColumn[] Columns = [];
    public SpmRow[] Rows = [];

    public readonly Dictionary<Column, int> ColumnDictionary = new();
    public readonly Dictionary<(Table Table, uint Index), int> RowDictionary = new();

    public uint Version => this.Header.Version;

    public override void LoadFile()
    {
        this.Header = this.Reader.ReadStructure<SpmHeader>();
        if (this.Header.Version != SupportedVersion)
            throw new InvalidDataException($"Cannot read SPM file of version 0x{this.Header.Version:X8}");

        var columnCount = (int) this.Header.ColumnCount;
        var rowCount = (int) this.Header.RowCount;

        this.Reader.BaseStream.Position = this.Header.ColumnsOffset << 2;
        var columnDefinitions = this.Reader.ReadStructuresAsArray<SpmColumnDefinition>(columnCount);
        this.Reader.BaseStream.Position = this.Header.RowsOffset << 2;
        var rowDefinitions = this.Reader.ReadStructuresAsArray<SpmRowDefinition>(rowCount);
        this.Reader.BaseStream.Position = this.Header.ValuesOffset << 2;
        var values = this.Reader.ReadStructuresAsArray<Value>(columnCount * rowCount);

        this.Rows = new SpmRow[rowCount];
        for (var i = 0; i < rowCount; i++) {
            var rowValues = values.AsSpan(columnCount * i, columnCount).ToArray();
            var isBlank = MemoryMarshal.AsBytes(rowValues.AsSpan()).IndexOfAnyExcept((byte) 0) < 0;

            this.Rows[i] = new(rowDefinitions[i].Table, rowDefinitions[i].Index, isBlank, rowValues);
            this.RowDictionary.TryAdd((rowDefinitions[i].Table, rowDefinitions[i].Index), i);
        }

        this.Columns = new SpmColumn[columnCount];
        for (var i = 0; i < columnCount; i++) {
            // A column is "constant" if all non-blank rows share the same value.
            var firstFound = false;
            var first = default(Value);
            var isConstant = true;
            foreach (var row in this.Rows) {
                if (row.IsBlank)
                    continue;

                if (!firstFound) {
                    first = row.Values[i];
                    firstFound = true;
                    continue;
                }

                if (row.Values[i] != first) {
                    isConstant = false;
                    break;
                }
            }

            this.Columns[i] = new(columnDefinitions[i].Name, columnDefinitions[i].Type, isConstant ? first : null);
            this.ColumnDictionary.TryAdd(columnDefinitions[i].Name, i);
        }
    }

    public bool TryGetValue(Table table, uint index, Column column, out Value value)
    {
        if (this.RowDictionary.TryGetValue((table, index), out var row)
            && this.ColumnDictionary.TryGetValue(column, out var col)) {
            value = this.Rows[row].Values[col];
            return true;
        }

        value = default;
        return false;
    }

    public static string DefaultSpmPath(Table table) => table switch {
        Table.Bg => "common/graphics/bg_shader_param.spm",
        Table.Chara => "common/graphics/chara_shader_param.spm",
        Table.Common => "common/graphics/common_shader_param.spm",
        _ => throw new ArgumentException($"Invalid SPM table 0x{(uint) table:X8}", nameof(table)),
    };

    public static string GetName(Table table) => table switch {
        Table.Null => "~",
        Table.Bg or Table.Chara or Table.Common => table.ToString(),
        _ => $"0x{(uint) table:X8}",
    };

    public static string GetName(Column column) => column switch {
        Column.Null => "~",
        Column.LightingType => "Lighting Type",
        Column.SubSurfaceProfileId => "Subsurface Profile ID",
        Column.SubSurfaceWidth => "Subsurface Width",
        Column.BackScatterPower => "Backscatter Power",
        Column.SheenRate => "Sheen Rate",
        Column.SheenTintRate => "Sheen Tint Rate",
        Column.SheenAperture => "Sheen Aperture",
        Column.UseSubSurfaceRate => "Use Subsurface Rate",
        Column.HairScatterColorShift => "Hair Scatter Color Shift",
        Column.HairSpecularShift => "Hair Specular Shift",
        Column.FurLength => "Fur Length",
        Column.HairRoughnessOffsetRate => "Hair Roughness Offset Rate",
        Column.SubSurfacePower => "Subsurface Power",
        Column.Reserve => "Reserve",
        Column.HairSpecularPrimaryShift => "Hair Specular Primary Shift",
        Column.HairSpecularSecondaryShift => "Hair Specular Secondary Shift",
        Column.HairSpecularBackScatterShift => "Hair Specular Backscatter Shift",
        Column.HairBackScatterRoughnessOffsetRate => "Hair Backscatter Roughness Offset Rate",
        Column.HairSecondaryRoughnessOffsetRate => "Hair Secondary Roughness Offset Rate",
        _ => $"0x{(uint) column:X8}",
    };

    public static string GetName(NamedValue value) => value switch {
        NamedValue.Null => "~",
        NamedValue.Default or NamedValue.Legacy or NamedValue.Hair or NamedValue.Half => value.ToString(),
        _ => $"0x{(uint) value:X8}",
    };

    /// <remarks>The non-zero values of that enum are the CRC32 of their uppercased names.</remarks>
    public enum Table : uint {
        Null = 0,
        Bg = 0x2AF7F9B4,
        Chara = 0xB9FDFB6C,
        Common = 0xA4D61674,
    }

    /// <remarks>
    /// The non-zero values of that enum are the CRC32 of their names. (Except for "Id" which shall be uppercased.)
    /// </remarks>
    public enum Column : uint {
        Null = 0,
        LightingType = 0xE8001A59,
        SubSurfaceProfileId = 0x8FB53404,
        SubSurfaceWidth = 0xF30D1232,
        BackScatterPower = 0x41338E94,
        SheenRate = 0xB1FEBD21,
        SheenTintRate = 0xB7867D05,
        SheenAperture = 0x5C30C2FC,
        UseSubSurfaceRate = 0x947205D5,
        HairScatterColorShift = 0x671C995B,
        HairSpecularShift = 0x6CD877F3,
        FurLength = 0x85DC1E5C,
        HairRoughnessOffsetRate = 0x8F6BA743,
        SubSurfacePower = 0xD49D56BD,
        Reserve = 0x4D310CC0,
        HairSpecularPrimaryShift = 0xF33FF064,
        HairSpecularSecondaryShift = 0xE0D24CB4,
        HairSpecularBackScatterShift = 0xA46A47BB,
        HairBackScatterRoughnessOffsetRate = 0x773AD7FB,
        HairSecondaryRoughnessOffsetRate = 0xA8C99005,
    }

    public enum ColumnType : uint {
        Float = 0,
        UInt = 1,
        Named = 2,
    }

    /// <remarks>The non-zero values of that enum are the CRC32 of their uppercased names.</remarks>
    public enum NamedValue : uint {
        Null = 0,
        Default = 0x417721BB,
        Legacy = 0x56F16FCB,
        Hair = 0x8B2653B1,
        Half = 0xEC8B7389,
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct Value : IEquatable<Value> {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(0)]
        public uint UInt;

        [FieldOffset(0)]
        public NamedValue Named;

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Value other && this.Equals(other);

        public override int GetHashCode() => this.UInt.GetHashCode();

        public bool Equals(Value other) => this.UInt == other.UInt;

        public static bool operator ==(Value left, Value right) => left.UInt == right.UInt;

        public static bool operator !=(Value left, Value right) => left.UInt != right.UInt;

        public string ToString(ColumnType type, IFormatProvider? formatProvider = null) => type switch {
            ColumnType.Float => this.Float.ToString(formatProvider),
            ColumnType.UInt => this.UInt.ToString(formatProvider),
            ColumnType.Named => GetName(this.Named),
            _ => $"0x{this.UInt:X8}",
        };

        public override string ToString() => $"0x{this.UInt:X8}";
    }

    public readonly record struct SpmColumn(Column Name, ColumnType Type, Value? ConstantValue) {
        public string DisplayName => GetName(this.Name);
    }

    public readonly record struct SpmRow(Table Table, uint Index, bool IsBlank, Value[] Values);

    [StructLayout(LayoutKind.Sequential)]
    public struct SpmHeader {
        public uint Version;
        public byte ColumnCount;
        public byte RowCount;
        public ushort ColumnsOffset;
        public ushort RowsOffset;
        public ushort ValuesOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpmColumnDefinition {
        public Column Name;
        public ColumnType Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpmRowDefinition {
        public Table Table;
        public uint Index;
    }
}
