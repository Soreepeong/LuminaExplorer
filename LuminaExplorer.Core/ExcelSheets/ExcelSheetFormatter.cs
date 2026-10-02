using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lumina.Data;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Formats the cells of a sheet for display, using its EXDSchema definition if available.</summary>
/// <remarks>Instances are immutable (other than the caches of the link resolver), and can be used from any thread.
/// </remarks>
public sealed class ExcelSheetFormatter {
    /// <summary>Sheets whose color fields are stored as RGBA, instead of ARGB as EXDSchema specifies.</summary>
    private static readonly HashSet<string> RgbaColorSheets = new(StringComparer.OrdinalIgnoreCase) { "UIColor" };

    private readonly bool _rgbaColors;

    public ExcelSheetFormatter(
        ExcelSheetSource source,
        ExcelSchemaBinding? binding,
        ExcelLinkResolver? linkResolver,
        Language language,
        ExcelCellFormatOptions options)
    {
        this.Source = source;
        this.Binding = binding is { IsMatched: true } ? binding : null;
        this.LinkResolver = linkResolver;
        this.Language = language;
        this.Options = options;
        this._rgbaColors = RgbaColorSheets.Contains(source.SheetName);
    }

    public ExcelSheetSource Source { get; }

    /// <summary>Gets the schema binding in use; <c>null</c> if the schema is not used, or does not match.</summary>
    public ExcelSchemaBinding? Binding { get; }

    /// <summary>Gets the link resolver; <c>null</c> if links are not resolved.</summary>
    public ExcelLinkResolver? LinkResolver { get; }

    public Language Language { get; }

    public ExcelCellFormatOptions Options { get; }

    /// <summary>Gets the field of a column, if the schema is in use.</summary>
    public ExcelSchemaColumn? GetField(int columnIndex) => this.Binding?.Fields[columnIndex];

    /// <summary>Gets the name of a column; the field name if the schema is in use, or the column index.</summary>
    public string GetColumnName(int columnIndex) =>
        this.GetField(columnIndex)?.Name ?? columnIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets the sheets that links in this sheet may point to.</summary>
    public IEnumerable<string> GetAllLinkTargets() =>
        this.Binding?.Fields.Where(x => x?.Kind == ExcelSchemaFieldKind.Link).SelectMany(x => x!.AllTargets)
            .Distinct() ?? [];

    /// <summary>Tests if the text of a column may contain non-numeric text, even if the column is numeric.</summary>
    public bool MayHaveFormattedText(int columnIndex) => this.GetField(columnIndex)?.Kind switch {
        ExcelSchemaFieldKind.Link => this.LinkResolver is not null,
        ExcelSchemaFieldKind.ModelId or ExcelSchemaFieldKind.Color => true,
        _ => false,
    };

    /// <summary>Formats a cell.</summary>
    public string GetText(ExcelSheetPage page, in ExcelSheetRow row, int columnIndex) =>
        this.GetText(page, row, columnIndex, out _);

    /// <summary>Formats a cell.</summary>
    /// <param name="page">Page containing the row.</param>
    /// <param name="row">Row.</param>
    /// <param name="columnIndex">Column index in the header.</param>
    /// <param name="pending">Set to <c>true</c> if a link target is still loading.</param>
    public string GetText(ExcelSheetPage page, in ExcelSheetRow row, int columnIndex, out bool pending)
    {
        pending = false;
        var column = this.Source.Columns[columnIndex];
        if (this.GetField(columnIndex) is not { } field || field.Kind == ExcelSchemaFieldKind.Scalar)
            return column.ReadDisplayText(page, row, this.Options);

        try {
            var value = column.ReadValue(page, row);
            var raw = ExcelSheetColumn.FormatValue(value, this.Options);
            switch (field.Kind) {
                case ExcelSchemaFieldKind.ModelId:
                    return FormatModelId(value) ?? raw;
                case ExcelSchemaFieldKind.Color:
                    return TryGetColor(value, out var color)
                        ? this._rgbaColors ? $"#{color:X8}" : FormatColor(color)
                        : raw;
                case ExcelSchemaFieldKind.Link when this.LinkResolver is not null && TryGetInteger(value, out var v):
                    var (state, sheet, text) = this.ResolveLink(page, row, field, v);
                    pending = state == ExcelLinkLookupState.Pending;
                    if (state != ExcelLinkLookupState.Found || text.Length == 0)
                        return raw;
                    return this.GetLinkCandidates(page, row, field).Length > 1
                        ? $"{raw} → {sheet}: {text}"
                        : $"{raw} → {text}";
                default:
                    return raw;
            }
        } catch (Exception e) {
            return $"<error: {e.Message}>";
        }
    }

    /// <summary>Gets additional information about a cell, for tooltips.</summary>
    public string? GetToolTip(ExcelSheetPage page, in ExcelSheetRow row, int columnIndex)
    {
        if (this.GetField(columnIndex) is not { } field)
            return null;

        try {
            var value = this.Source.Columns[columnIndex].ReadValue(page, row);
            switch (field.Kind) {
                case ExcelSchemaFieldKind.Icon when TryGetInteger(value, out var id) && id > 0:
                    return GetIconPath(id);
                case ExcelSchemaFieldKind.ModelId when value is uint or int: {
                    var v = value is int i ? unchecked((uint) i) : (uint) value;
                    return $"Model {v & 0xFFFF}, variant {(v >> 16) & 0xFF}, stain {v >> 24}";
                }
                case ExcelSchemaFieldKind.ModelId when value is ulong or long: {
                    var v = value is long l ? unchecked((ulong) l) : (ulong) value;
                    return $"{v & 0xFFFF}, {(v >> 16) & 0xFFFF}, {(v >> 32) & 0xFFFF}, {v >> 48} " +
                        "(skeleton/model, model/variant, variant, stain)";
                }
                case ExcelSchemaFieldKind.Color when TryGetColor(value, out var color): {
                    var argb = this.ToArgb(color);
                    return (this._rgbaColors ? "RGBA: " : string.Empty) +
                        $"R={(argb >> 16) & 0xFF} G={(argb >> 8) & 0xFF} B={argb & 0xFF}" +
                        (argb >> 24 == 0 && !this._rgbaColors ? string.Empty : $" A={argb >> 24}");
                }
                case ExcelSchemaFieldKind.Link when TryGetInteger(value, out var target): {
                    var candidates = this.GetLinkCandidates(page, row, field);
                    if (candidates.Length == 0)
                        return field.Condition is not null ? "No target sheet for the current condition" : null;
                    if (this.LinkResolver is null)
                        return string.Join(" / ", candidates.Select(x => $"{x}#{target}"));

                    var (state, sheet, text) = this.ResolveLink(page, row, field, target);
                    return state switch {
                        ExcelLinkLookupState.Found => $"{sheet}#{target}: {text}",
                        ExcelLinkLookupState.Pending => $"Loading {string.Join(", ", candidates)}...",
                        _ => $"Row {target} not found in {string.Join(", ", candidates)}",
                    };
                }
            }
        } catch (Exception e) {
            return e.Message;
        }

        return null;
    }

    /// <summary>Gets the color of a cell, if it is a color field.</summary>
    /// <returns>The color in ARGB, with zero alpha replaced by opaque; or <c>null</c>.</returns>
    public uint? GetSwatchColor(ExcelSheetPage page, in ExcelSheetRow row, int columnIndex)
    {
        if (this.GetField(columnIndex)?.Kind != ExcelSchemaFieldKind.Color)
            return null;
        try {
            if (!TryGetColor(this.Source.Columns[columnIndex].ReadValue(page, row), out var color))
                return null;
            var argb = this.ToArgb(color);
            return argb >> 24 == 0 ? argb | 0xFF000000u : argb;
        } catch (Exception) {
            return null;
        }
    }

    private uint ToArgb(uint color) => this._rgbaColors ? (color >> 8) | (color << 24) : color;

    public static string GetIconPath(long iconId) =>
        string.Create(CultureInfo.InvariantCulture, $"ui/icon/{iconId / 1000 * 1000:D6}/{iconId:D6}.tex");

    /// <summary>Formats a packed model ID.</summary>
    /// <remarks>
    /// <para>32-bit: <c>u16 model, u8 variant, u8 stain</c>, shown as <c>0100 v1</c>.</para>
    /// <para>64-bit: four <c>u16</c>s. Weapons use <c>skeleton, model, variant, stain</c> (shown as
    /// <c>w0101b0003 v1</c>), and equipment uses <c>model, variant, 0, stain</c> (shown as <c>e0100 v1</c>).</para>
    /// </remarks>
    public static string? FormatModelId(object? value)
    {
        switch (value) {
            case int or uint: {
                var v = value is int i ? unchecked((uint) i) : (uint) value;
                if (v == 0)
                    return "0";
                var s = $"{v & 0xFFFF:D4} v{(v >> 16) & 0xFF}";
                return v >> 24 == 0 ? s : $"{s} s{v >> 24}";
            }
            case long or ulong: {
                var v = value is long l ? unchecked((ulong) l) : (ulong) value;
                if (v == 0)
                    return "0";
                var a = v & 0xFFFF;
                var b = (v >> 16) & 0xFFFF;
                var c = (v >> 32) & 0xFFFF;
                var d = v >> 48;
                var s = c == 0 ? $"e{a:D4} v{b}" : $"w{a:D4}b{b:D4} v{c}";
                return d == 0 ? s : $"{s} s{d}";
            }
            default:
                return null;
        }
    }

    /// <summary>Formats a color stored as ARGB (usually with zero alpha) as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.
    /// </summary>
    public static string FormatColor(uint argb) => argb >> 24 == 0 ? $"#{argb:X6}" : $"#{argb:X8}";

    private static bool TryGetColor(object? value, out uint argb)
    {
        switch (value) {
            case uint u:
                argb = u;
                return true;
            case int i:
                argb = unchecked((uint) i);
                return true;
            default:
                argb = 0;
                return false;
        }
    }

    internal static bool TryGetInteger(object? value, out long result)
    {
        switch (value) {
            case sbyte v: result = v; return true;
            case byte v: result = v; return true;
            case short v: result = v; return true;
            case ushort v: result = v; return true;
            case int v: result = v; return true;
            case uint v: result = v; return true;
            case long v: result = v; return true;
            case ulong v: result = unchecked((long) v); return true;
            case bool v: result = v ? 1 : 0; return true;
            default: result = 0; return false;
        }
    }

    /// <summary>Gets the sheets that a link cell may point to, considering conditions.</summary>
    private string[] GetLinkCandidates(ExcelSheetPage page, in ExcelSheetRow row, ExcelSchemaColumn field)
    {
        if (field.Condition is not { } condition)
            return field.Targets;

        var switchColumn = this.Binding?.FindTopLevelColumn(condition.SwitchField) ?? -1;
        if (switchColumn == -1 ||
            !TryGetInteger(this.Source.Columns[switchColumn].ReadValue(page, row), out var switchValue))
            return [];

        return condition.Cases.TryGetValue(switchValue, out var targets) ? targets : [];
    }

    private (ExcelLinkLookupState State, string Sheet, string Text) ResolveLink(
        ExcelSheetPage page,
        in ExcelSheetRow row,
        ExcelSchemaColumn field,
        long value)
    {
        if (this.LinkResolver is not { } resolver || value is < 0 or > uint.MaxValue)
            return (ExcelLinkLookupState.NotFound, string.Empty, string.Empty);

        // The first target sheet containing the row wins; earlier targets must be known not to contain it.
        var result = (State: ExcelLinkLookupState.NotFound, Sheet: string.Empty, Text: string.Empty);
        foreach (var sheet in this.GetLinkCandidates(page, row, field)) {
            switch (resolver.TryGetRowText(sheet, this.Language, (uint) value, out var text)) {
                case ExcelLinkLookupState.Found:
                    return (ExcelLinkLookupState.Found, sheet, text);
                case ExcelLinkLookupState.Pending:
                    result.State = ExcelLinkLookupState.Pending;
                    break;
            }

            if (result.State == ExcelLinkLookupState.Pending)
                break;
        }

        return result;
    }
}
