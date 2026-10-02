using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Text.ReadOnly;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.Core.ExcelSheets.Index;

/// <summary>Builds an <see cref="ExcelValueIndex"/> by reading every sheet listed in <c>exd/root.exl</c>.</summary>
public static class ExcelValueIndexBuilder {
    /// <summary>Builds the index.</summary>
    /// <param name="vfs">Virtual file system.</param>
    /// <param name="root">Root folder of the game data, containing <c>exd/</c>.</param>
    /// <param name="language">Preferred language. Sheets without it are read in their language-neutral data if any,
    /// or otherwise in their first declared language.</param>
    /// <param name="versionKey">Key of the game data version, stored in the index.</param>
    /// <param name="progress">Called from worker threads as sheets are done.</param>
    /// <param name="maxParallelism">Number of sheets to read at once.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="schemaSet">Sheet definitions, used to find numeric cells that refer to game paths; the branch should
    /// be a part of <paramref name="versionKey"/>.</param>
    /// <param name="derivedReferences">Finds the game paths that numeric cells refer to; if <c>null</c>, or if
    /// <paramref name="schemaSet"/> is <c>null</c>, only string cells are indexed.</param>
    public static async Task<ExcelValueIndex> BuildAsync(
        IVirtualFileSystem vfs,
        IVirtualFolder root,
        Language language,
        string versionKey,
        Action<ExcelValueIndexProgress>? progress = null,
        int maxParallelism = 0,
        CancellationToken cancellationToken = default,
        ExcelSchemaSet? schemaSet = null,
        ExcelDerivedReferences? derivedReferences = null)
    {
        if (schemaSet is null)
            derivedReferences = null;
        if (maxParallelism <= 0)
            maxParallelism = Math.Max(1, Environment.ProcessorCount / 2);

        var exlFile = await vfs.LocateFile(root, "exd/root.exl")
            ?? throw new InvalidOperationException("exd/root.exl could not be found.");
        ExcelListFile exl;
        using (var lookup = vfs.GetLookup(exlFile))
            exl = await lookup.AsFileResource<ExcelListFile>(cancellationToken);

        var sheetNames = exl.ExdMap.Keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sheetNames.Length > ushort.MaxValue + 1)
            throw new NotSupportedException("Too many sheets.");

        var state = new BuildState();
        var sheetHasSubrows = new bool[sheetNames.Length];
        var done = 0;
        progress?.Invoke(new(0, sheetNames.Length));

        await Parallel.ForEachAsync(
            Enumerable.Range(0, sheetNames.Length),
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationToken },
            async (sheetIndex, ct) => {
                try {
                    var (cells, hasSubrows) = await ReadSheet(
                        vfs,
                        root,
                        sheetNames[sheetIndex],
                        language,
                        schemaSet,
                        derivedReferences,
                        ct);
                    sheetHasSubrows[sheetIndex] = hasSubrows;
                    if (cells.Count > 0)
                        state.Add((ushort) sheetIndex, cells);
                } catch (Exception e) when (e is not OperationCanceledException) {
                    // A broken sheet should not prevent indexing the others.
                    Interlocked.Increment(ref state.FailedSheets);
                }

                progress?.Invoke(new(Interlocked.Increment(ref done), sheetNames.Length));
            });

        return state.ToIndex(versionKey, language, sheetNames, sheetHasSubrows);
    }

    private static async Task<(List<CellValue> Cells, bool HasSubrows)> ReadSheet(
        IVirtualFileSystem vfs,
        IVirtualFolder root,
        string sheetName,
        Language preferredLanguage,
        ExcelSchemaSet? schemaSet,
        ExcelDerivedReferences? derivedReferences,
        CancellationToken cancellationToken)
    {
        var result = new List<CellValue>();
        if (await ExcelSheetSource.OpenByName(vfs, root, sheetName, cancellationToken) is not { } source)
            return (result, false);

        var plan = derivedReferences?.CreatePlan(source, ExcelSchemaBinding.Create(source, schemaSet));
        var derived = new List<ExcelDerivedReference>();
        var stringColumns = source.Columns.Where(x => x.IsString).ToArray();
        if (stringColumns.Length == 0 && plan is null)
            return (result, source.HasSubrows);

        var languages = source.DeclaredLanguages;
        var language = languages.Contains(preferredLanguage) ? preferredLanguage
            : languages.Contains(Language.None) || languages.Count == 0 ? Language.None
            : languages[0];

        for (var pageIndex = 0; pageIndex < source.Pages.Count; pageIndex++) {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await source.GetPage(language, pageIndex);
            if (page is null)
                continue;

            foreach (var row in page.Rows) {
                foreach (var column in stringColumns) {
                    if (column.ReadValue(page, row) is not ReadOnlySeString s || s.IsEmpty)
                        continue;

                    var text = ToPlainText(s);
                    if (text.Length > 0)
                        result.Add(new(text, row.RowId, row.SubrowId, (ushort) column.Index, null));
                }

                if (plan is null)
                    continue;

                derived.Clear();
                derivedReferences!.Derive(plan, page, row, derived);
                foreach (var d in derived)
                    result.Add(new(d.Path, row.RowId, row.SubrowId, (ushort) d.ColumnIndex, d.SourceText));
            }
        }

        return (result, source.HasSubrows);
    }

    /// <summary>A value to index.</summary>
    /// <param name="Text">Text of the cell, or the derived game path.</param>
    /// <param name="RowId">Row ID.</param>
    /// <param name="SubrowId">Subrow ID.</param>
    /// <param name="Column">Column index.</param>
    /// <param name="Source">For derived game paths, the value of the cell as text.</param>
    private readonly record struct CellValue(string Text, uint RowId, ushort SubrowId, ushort Column, string? Source);

    /// <summary>Gets the text payloads of a SeString as a single line.</summary>
    private static string ToPlainText(ReadOnlySeString s)
    {
        string text;
        try {
            text = s.ExtractText();
        } catch (Exception) {
            return string.Empty;
        }

        if (text.AsSpan().IndexOfAny("\r\n\0") >= 0) {
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
                sb.Append(c is '\r' or '\n' or '\0' ? ' ' : c);
            text = sb.ToString();
        }

        return text.Trim();
    }

    private sealed class BuildState {
        private readonly object _lock = new();
        private readonly Dictionary<string, int> _valueIds = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];
        private readonly List<(int Value, ushort Sheet, uint Row, ushort Subrow, ushort Column, int Source)> _cells =
            [];

        // Texts that derived values were derived from.
        private readonly Dictionary<string, int> _sourceIds = new(StringComparer.Ordinal);
        private readonly List<string> _sources = [];

        public int FailedSheets;

        public void Add(ushort sheet, List<CellValue> cells)
        {
            lock (this._lock) {
                foreach (var (text, rowId, subrowId, column, source) in cells) {
                    if (!this._valueIds.TryGetValue(text, out var id)) {
                        this._valueIds.Add(text, id = this._values.Count);
                        this._values.Add(text);
                    }

                    var sourceId = -1;
                    if (source is not null && !this._sourceIds.TryGetValue(source, out sourceId)) {
                        this._sourceIds.Add(source, sourceId = this._sources.Count);
                        this._sources.Add(source);
                    }

                    this._cells.Add((id, sheet, rowId, subrowId, column, sourceId));
                }
            }
        }

        public ExcelValueIndex ToIndex(
            string versionKey,
            Language language,
            string[] sheetNames,
            bool[] sheetHasSubrows)
        {
            var valueCount = this._values.Count;

            // Sort the values, so that the index does not depend on the order the sheets were read in.
            var order = Enumerable.Range(0, valueCount).ToArray();
            var valuesArray = this._values.ToArray();
            Array.Sort(valuesArray, order, StringComparer.Ordinal);
            var newIdOf = new int[valueCount];
            for (var i = 0; i < valueCount; i++)
                newIdOf[order[i]] = i;

            var lowerOffsets = new int[valueCount + 1];
            var textOffsets = new int[valueCount + 1];
            var lowerSize = 0L;
            var textSize = 0L;
            var lowerParts = new byte[valueCount][];
            for (var i = 0; i < valueCount; i++) {
                lowerOffsets[i] = checked((int) lowerSize);
                textOffsets[i] = checked((int) textSize);
                lowerParts[i] = ExcelValueIndex.ToLowerBytes(valuesArray[i]);
                lowerSize += lowerParts[i].Length + 1;
                textSize += Encoding.UTF8.GetByteCount(valuesArray[i]) + 1;
            }

            lowerOffsets[valueCount] = checked((int) lowerSize);
            textOffsets[valueCount] = checked((int) textSize);

            var lower = new byte[lowerSize];
            var text = new byte[textSize];
            for (var i = 0; i < valueCount; i++) {
                lowerParts[i].CopyTo(lower, lowerOffsets[i]);
                Encoding.UTF8.GetBytes(valuesArray[i], text.AsSpan(textOffsets[i]));
            }

            // Counting sort of the cells by value; within a value, by sheet, row, subrow, and column.
            var cells = this._cells;
            cells.Sort(
                (a, b) => a.Sheet != b.Sheet ? a.Sheet.CompareTo(b.Sheet)
                    : a.Row != b.Row ? a.Row.CompareTo(b.Row)
                    : a.Subrow != b.Subrow ? a.Subrow.CompareTo(b.Subrow)
                    : a.Column != b.Column ? a.Column.CompareTo(b.Column)
                    : a.Source.CompareTo(b.Source));
            var cellOffsets = new int[valueCount + 1];
            foreach (var c in cells)
                cellOffsets[newIdOf[c.Value] + 1]++;
            for (var i = 0; i < valueCount; i++)
                cellOffsets[i + 1] += cellOffsets[i];

            var cellSheets = new ushort[cells.Count];
            var cellRows = new uint[cells.Count];
            var cellSubrows = new ushort[cells.Count];
            var cellColumns = new ushort[cells.Count];
            var cellSources = new int[cells.Count];
            var next = cellOffsets[..^1];
            foreach (var c in cells) {
                var j = next[newIdOf[c.Value]]++;
                cellSheets[j] = c.Sheet;
                cellRows[j] = c.Row;
                cellSubrows[j] = c.Subrow;
                cellColumns[j] = c.Column;
                cellSources[j] = c.Source;
            }

            // Sorted as well, for the same reason as the values.
            var sourceOrder = Enumerable.Range(0, this._sources.Count).ToArray();
            var sourceTexts = this._sources.ToArray();
            Array.Sort(sourceTexts, sourceOrder, StringComparer.Ordinal);
            var newSourceIdOf = new int[sourceTexts.Length];
            for (var i = 0; i < sourceTexts.Length; i++)
                newSourceIdOf[sourceOrder[i]] = i;

            var derivedCells = new List<int>();
            var derivedSources = new List<int>();
            for (var i = 0; i < cellSources.Length; i++) {
                if (cellSources[i] < 0)
                    continue;
                derivedCells.Add(i);
                derivedSources.Add(newSourceIdOf[cellSources[i]]);
            }

            return new(
                versionKey,
                language,
                sheetNames,
                sheetHasSubrows,
                lower,
                lowerOffsets,
                text,
                textOffsets,
                cellOffsets,
                cellSheets,
                cellRows,
                cellSubrows,
                cellColumns,
                derivedCells.ToArray(),
                derivedSources.ToArray(),
                sourceTexts);
        }
    }
}
