using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Lumina.Data.Structs.Excel;
using Lumina.Text.ReadOnly;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Describes a column of an Excel sheet, and decodes and formats its cells.</summary>
public sealed class ExcelSheetColumn {
    public ExcelSheetColumn(int index, ExcelColumnDefinition definition)
    {
        this.Index = index;
        this.Type = definition.Type;
        this.Offset = definition.Offset;
        this.Name = $"{index}";
    }

    /// <summary>Gets the index of the column in the header.</summary>
    public int Index { get; }

    public ExcelColumnDataType Type { get; }

    /// <summary>Gets the byte offset of the column in a row's fixed size data.</summary>
    public ushort Offset { get; }

    /// <summary>Gets or sets the display name; defaults to the column index.</summary>
    public string Name { get; set; }

    /// <summary>Gets the bit index for packed booleans, or <c>-1</c>.</summary>
    public int Bit => this.Type is >= ExcelColumnDataType.PackedBool0 and <= ExcelColumnDataType.PackedBool7
        ? this.Type - ExcelColumnDataType.PackedBool0
        : -1;

    public bool IsString => this.Type == ExcelColumnDataType.String;

    public bool IsNumeric => this.Type is ExcelColumnDataType.Int8 or ExcelColumnDataType.UInt8
        or ExcelColumnDataType.Int16 or ExcelColumnDataType.UInt16
        or ExcelColumnDataType.Int32 or ExcelColumnDataType.UInt32
        or ExcelColumnDataType.Int64 or ExcelColumnDataType.UInt64
        or ExcelColumnDataType.Float32;

    /// <summary>Gets a short name of the type, e.g. <c>u32</c>, <c>str</c>, or <c>bit3</c>.</summary>
    public string ShortTypeName => GetShortTypeName(this.Type);

    /// <summary>Gets the location as text, e.g. <c>@1A</c> or <c>@1A.3</c>.</summary>
    public string LocationText => this.Bit == -1 ? $"@{this.Offset:X}" : $"@{this.Offset:X}.{this.Bit}";

    public string Description =>
        $"Column {this.Index}\nType: {this.Type}\nOffset: 0x{this.Offset:X} ({this.Offset})" +
        (this.Bit == -1 ? string.Empty : $"\nBit: {this.Bit} (mask 0x{1 << this.Bit:X2})");

    public static string GetShortTypeName(ExcelColumnDataType type) => type switch {
        ExcelColumnDataType.String => "str",
        ExcelColumnDataType.Bool => "bool",
        ExcelColumnDataType.Int8 => "i8",
        ExcelColumnDataType.UInt8 => "u8",
        ExcelColumnDataType.Int16 => "i16",
        ExcelColumnDataType.UInt16 => "u16",
        ExcelColumnDataType.Int32 => "i32",
        ExcelColumnDataType.UInt32 => "u32",
        ExcelColumnDataType.Float32 => "f32",
        ExcelColumnDataType.Int64 => "i64",
        ExcelColumnDataType.UInt64 => "u64",
        >= ExcelColumnDataType.PackedBool0 and <= ExcelColumnDataType.PackedBool7 =>
            $"bit{type - ExcelColumnDataType.PackedBool0}",
        _ => $"?{(int) type}",
    };

    /// <summary>Reads the value of this column.</summary>
    /// <param name="page">Page containing the row.</param>
    /// <param name="row">Row to read from.</param>
    /// <returns>
    /// The value: <see cref="ReadOnlySeString"/>, <see cref="bool"/>, an integer type, <see cref="float"/>, or
    /// <c>null</c> if the column lies outside the row data.
    /// </returns>
    public object? ReadValue(ExcelSheetPage page, in ExcelSheetRow row)
    {
        var data = page.Data;
        var o = row.FixedDataOffset + this.Offset;
        var size = this.Type switch {
            ExcelColumnDataType.Int16 or ExcelColumnDataType.UInt16 => 2,
            ExcelColumnDataType.String or ExcelColumnDataType.Int32 or ExcelColumnDataType.UInt32
                or ExcelColumnDataType.Float32 => 4,
            ExcelColumnDataType.Int64 or ExcelColumnDataType.UInt64 => 8,
            _ => 1,
        };
        if (o < 0 || o + size > data.Length)
            return null;

        var span = data.AsSpan(o, size);
        switch (this.Type) {
            case ExcelColumnDataType.String: {
                var start = row.StringDataOffset + (long) BinaryPrimitives.ReadUInt32BigEndian(span);
                if (start < 0 || start >= data.Length)
                    return new ReadOnlySeString();
                var len = data.AsSpan((int) start).IndexOf((byte) 0);
                if (len < 0)
                    len = data.Length - (int) start;
                return new ReadOnlySeString(data.AsMemory((int) start, len));
            }
            case ExcelColumnDataType.Bool:
                return span[0] != 0;
            case ExcelColumnDataType.Int8:
                return unchecked((sbyte) span[0]);
            case ExcelColumnDataType.UInt8:
                return span[0];
            case ExcelColumnDataType.Int16:
                return BinaryPrimitives.ReadInt16BigEndian(span);
            case ExcelColumnDataType.UInt16:
                return BinaryPrimitives.ReadUInt16BigEndian(span);
            case ExcelColumnDataType.Int32:
                return BinaryPrimitives.ReadInt32BigEndian(span);
            case ExcelColumnDataType.UInt32:
                return BinaryPrimitives.ReadUInt32BigEndian(span);
            case ExcelColumnDataType.Float32:
                return BinaryPrimitives.ReadSingleBigEndian(span);
            case ExcelColumnDataType.Int64:
                return BinaryPrimitives.ReadInt64BigEndian(span);
            case ExcelColumnDataType.UInt64:
                return BinaryPrimitives.ReadUInt64BigEndian(span);
            case >= ExcelColumnDataType.PackedBool0 and <= ExcelColumnDataType.PackedBool7:
                return (span[0] & (1 << this.Bit)) != 0;
            default:
                // Unknown type; show the raw byte.
                return span[0];
        }
    }

    /// <summary>Reads and formats the value of this column for display.</summary>
    public string ReadDisplayText(ExcelSheetPage page, in ExcelSheetRow row, ExcelCellFormatOptions options)
    {
        try {
            return FormatValue(this.ReadValue(page, row), options);
        } catch (Exception e) {
            return $"<error: {e.Message}>";
        }
    }

    /// <summary>Formats a value returned from <see cref="ReadValue"/>.</summary>
    public static string FormatValue(object? value, ExcelCellFormatOptions options) => value switch {
        null => string.Empty,
        ReadOnlySeString s => FormatSeString(s, options),
        bool b => b ? "true" : "false",
        float f => f.ToString(CultureInfo.InvariantCulture),
        sbyte v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X2}",
        byte v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X2}",
        short v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X4}",
        ushort v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X4}",
        int v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X8}",
        uint v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X8}",
        long v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X16}",
        ulong v when options.HasFlag(ExcelCellFormatOptions.HexIntegers) => $"0x{v:X16}",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Formats a SeString into a single line of text.</summary>
    /// <remarks>
    /// With <see cref="ExcelCellFormatOptions.TextOnly"/>, only the text payloads are kept and line breaks become
    /// <c>⏎</c>; otherwise, the macro representation is used (e.g. <c>&lt;if(...)&gt;</c>, <c>&lt;br&gt;</c>).
    /// </remarks>
    public static string FormatSeString(ReadOnlySeString s, ExcelCellFormatOptions options)
    {
        if (s.IsEmpty)
            return string.Empty;
        try {
            if (options.HasFlag(ExcelCellFormatOptions.TextOnly)) {
                var text = s.ExtractText();
                return text.Contains('\n') || text.Contains('\r')
                    ? text.Replace("\r\n", "⏎").Replace('\n', '⏎').Replace('\r', '⏎')
                    : text;
            }

            return s.ToMacroString();
        } catch (Exception) {
            var sb = new StringBuilder("<invalid:");
            foreach (var b in s.Data.Span)
                sb.Append(CultureInfo.InvariantCulture, $" {b:X2}");
            return sb.Append('>').ToString();
        }
    }
}

[Flags]
public enum ExcelCellFormatOptions {
    None = 0,

    /// <summary>Show only text payloads of SeStrings, instead of their macro representation.</summary>
    TextOnly = 1 << 0,

    /// <summary>Show integers in hexadecimal.</summary>
    HexIntegers = 1 << 1,
}
