using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Location of a row (or a subrow) inside an <see cref="ExcelSheetPage"/>.</summary>
/// <param name="RowId">Row ID.</param>
/// <param name="SubrowId">Subrow ID; always 0 for sheets without subrows.</param>
/// <param name="FixedDataOffset">Offset of the fixed size data in <see cref="ExcelSheetPage.Data"/>.</param>
/// <param name="StringDataOffset">Offset that string column values are relative to.</param>
public readonly record struct ExcelSheetRow(uint RowId, ushort SubrowId, int FixedDataOffset, int StringDataOffset);

/// <summary>A parsed data page (.exd) of an Excel sheet.</summary>
public sealed class ExcelSheetPage {
    public ExcelSheetPage(
        int pageIndex,
        Language language,
        ExcelVariant variant,
        int fixedDataSize,
        ExcelDataFile dataFile)
    {
        this.PageIndex = pageIndex;
        this.Language = language;
        this.Data = dataFile.Data;

        var rows = new List<ExcelSheetRow>(dataFile.RowData.Count);
        var data = this.Data;
        foreach (var (rowId, offset) in dataFile.RowData.OrderBy(x => x.Key)) {
            var o = checked((int) offset.Offset);
            if (o < 0 || o + 6 > data.Length)
                continue;

            // ExcelDataRowHeader: u32be dataSize, u16be rowCount
            var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(o + 4, 2));
            var rowDataOffset = o + 6;
            if (variant == ExcelVariant.Subrows) {
                // Each subrow: u16be subrowId, followed by the fixed size data.
                // String offsets are relative to the end of the fixed size data of all subrows of the row.
                // Note: Lumina 7.7.1's RawSubrow uses the end of each subrow instead, which yields garbage for
                // subrows other than the last one (e.g. CustomTalkDefineClient, QuestDefineClient).
                var stringDataOffset = rowDataOffset + count * (fixedDataSize + 2);
                for (var i = 0; i < count; i++) {
                    var so = rowDataOffset + i * (fixedDataSize + 2);
                    if (so + 2 > data.Length)
                        break;
                    var subrowId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(so, 2));
                    rows.Add(new(rowId, subrowId, so + 2, stringDataOffset));
                }
            } else {
                rows.Add(new(rowId, 0, rowDataOffset, rowDataOffset + fixedDataSize));
            }
        }

        this.Rows = rows.ToArray();
    }

    public int PageIndex { get; }

    public Language Language { get; }

    public byte[] Data { get; }

    /// <summary>Gets the rows, sorted by row ID and then by subrow index.</summary>
    public ExcelSheetRow[] Rows { get; }
}
