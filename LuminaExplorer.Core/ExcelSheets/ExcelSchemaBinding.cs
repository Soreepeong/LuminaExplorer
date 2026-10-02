using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Data.Structs.Excel;

namespace LuminaExplorer.Core.ExcelSheets;

public enum ExcelSchemaBindingStatus {
    /// <summary>No schema set is available (yet).</summary>
    NoSchemaSet,

    /// <summary>The schema set does not contain a definition of the sheet.</summary>
    Missing,

    /// <summary>The definition of the sheet could not be parsed.</summary>
    Invalid,

    /// <summary>The number of fields in the definition differs from the number of columns of the sheet.</summary>
    CountMismatch,

    /// <summary>All columns have a field.</summary>
    Matched,
}

/// <summary>Associates the columns of a sheet with the fields of its EXDSchema definition.</summary>
/// <remarks>
/// EXDSchema fields are listed in the order of their offsets in the row, which may differ from the order of the
/// column definitions in the header file. Columns are therefore sorted by offset and then by the bit index of packed
/// booleans (lowest bit first) before being paired with the flattened fields.
/// </remarks>
public sealed class ExcelSchemaBinding {
    private readonly Dictionary<string, int> _topLevelColumns;

    private ExcelSchemaBinding(
        ExcelSchemaBindingStatus status,
        string message,
        ExcelSchemaSheet? schema,
        ExcelSchemaColumn?[] fields)
    {
        this.Status = status;
        this.Message = message;
        this.Schema = schema;
        this.Fields = fields;
        this._topLevelColumns = new(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++) {
            if (fields[i] is { } f && f.Name == f.TopLevelName)
                this._topLevelColumns.TryAdd(f.Name, i);
        }
    }

    public ExcelSchemaBindingStatus Status { get; }

    /// <summary>Gets a short description of the status, for display.</summary>
    public string Message { get; }

    /// <summary>Gets the sheet definition, if found (even if it does not match).</summary>
    public ExcelSchemaSheet? Schema { get; }

    /// <summary>Gets the field of each column, indexed by the column index in the header; all <c>null</c> unless
    /// <see cref="Status"/> is <see cref="ExcelSchemaBindingStatus.Matched"/>.</summary>
    public ExcelSchemaColumn?[] Fields { get; }

    public bool IsMatched => this.Status == ExcelSchemaBindingStatus.Matched;

    /// <summary>Gets the columns in the order that EXDSchema lists fields: by offset, and then by bit.</summary>
    public static ExcelSheetColumn[] GetColumnsInSchemaOrder(IEnumerable<ExcelSheetColumn> columns) =>
        columns.OrderBy(x => x.Offset).ThenBy(x => x.Bit).ThenBy(x => x.Index).ToArray();

    public static ExcelSchemaBinding Create(ExcelSheetSource source, ExcelSchemaSet? set) =>
        Create(source.SheetName, source.Columns, set);

    public static ExcelSchemaBinding Create(string sheetName, ExcelSheetColumn[] columns, ExcelSchemaSet? set)
    {
        var fields = new ExcelSchemaColumn?[columns.Length];
        if (set is null)
            return new(ExcelSchemaBindingStatus.NoSchemaSet, "No schema", null, fields);

        var schema = set.TryGetSheet(sheetName, out var error);
        if (schema is null) {
            return error is null
                ? new(ExcelSchemaBindingStatus.Missing, $"No schema for {sheetName} in {set.Branch}", null, fields)
                : new(ExcelSchemaBindingStatus.Invalid, $"Invalid schema ({set.Branch}): {error}", null, fields);
        }

        if (schema.Columns.Length != columns.Length) {
            return new(
                ExcelSchemaBindingStatus.CountMismatch,
                $"Schema mismatch ({set.Branch}): {schema.Columns.Length} fields for {columns.Length} columns; " +
                "names not applied",
                schema,
                fields);
        }

        var ordered = GetColumnsInSchemaOrder(columns);
        for (var i = 0; i < ordered.Length; i++)
            fields[ordered[i].Index] = schema.Columns[i];

        return new(ExcelSchemaBindingStatus.Matched, $"Schema: {set.Branch}", schema, fields);
    }

    /// <summary>Finds the column of a top-level non-array field.</summary>
    /// <returns>Column index, or <c>-1</c>.</returns>
    public int FindTopLevelColumn(string name) => this._topLevelColumns.GetValueOrDefault(name, -1);

    /// <summary>Finds the column best representing a row, for showing links to this sheet.</summary>
    /// <returns>Column index, or <c>-1</c>.</returns>
    public int FindDisplayColumn(ExcelSheetColumn[] columns)
    {
        if (this.IsMatched) {
            if (this.Schema?.DisplayField is { } displayField) {
                for (var i = 0; i < this.Fields.Length; i++) {
                    if (this.Fields[i]?.TopLevelName == displayField)
                        return i;
                }
            }

            foreach (var name in (string[]) ["Name", "Singular", "Text", "Description"]) {
                var i = this.FindTopLevelColumn(name);
                if (i != -1 && columns[i].IsString)
                    return i;
            }
        }

        return GetColumnsInSchemaOrder(columns).FirstOrDefault(x => x.IsString)?.Index ?? -1;
    }

    /// <summary>Counts the columns whose field kind is implausible for the column type.</summary>
    /// <remarks>Links, icons, model IDs, and colors must be integers; model IDs and colors at least 32 bits.</remarks>
    public int CountTypeDisagreements(ExcelSheetColumn[] columns)
    {
        var count = 0;
        for (var i = 0; i < this.Fields.Length; i++) {
            if (this.Fields[i] is not { } f)
                continue;
            var t = columns[i].Type;
            var bad = f.Kind switch {
                ExcelSchemaFieldKind.Link or ExcelSchemaFieldKind.Icon => !columns[i].IsNumeric ||
                    t == ExcelColumnDataType.Float32,
                ExcelSchemaFieldKind.ModelId => t is not (ExcelColumnDataType.UInt32 or ExcelColumnDataType.Int32
                    or ExcelColumnDataType.UInt64 or ExcelColumnDataType.Int64),
                ExcelSchemaFieldKind.Color => t is not (ExcelColumnDataType.UInt32 or ExcelColumnDataType.Int32),
                _ => false,
            };
            if (bad)
                count++;
        }

        return count;
    }
}
