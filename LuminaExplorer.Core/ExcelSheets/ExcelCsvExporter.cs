using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Writes the rows of an Excel sheet as CSV (UTF-8 with BOM, CRLF line endings).</summary>
public static class ExcelCsvExporter {
    /// <summary>Gets the CSV headers for a sheet: row ID, subrow ID (for sheets with subrows), and one per column;
    /// the field name if the schema is in use, or the column index, type, and location otherwise.</summary>
    public static string[] GetHeaders(ExcelSheetFormatter formatter)
    {
        var source = formatter.Source;
        var headers = new List<string>(source.Columns.Length + 2) { "RowId" };
        if (source.HasSubrows)
            headers.Add("Subrow");
        foreach (var column in source.Columns) {
            headers.Add(
                formatter.GetField(column.Index) is { } field
                    ? field.Name
                    : $"{formatter.GetColumnName(column.Index)} {column.ShortTypeName} {column.LocationText}");
        }

        return headers.ToArray();
    }

    /// <summary>Quotes a value if needed.</summary>
    public static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;

    /// <summary>Writes the given rows.</summary>
    /// <param name="stream">Stream to write to; left open.</param>
    /// <param name="formatter">Formatter of the cells.</param>
    /// <param name="rows">Rows to write.</param>
    /// <param name="headers">Headers; see <see cref="GetHeaders"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows written.</returns>
    public static int Write(
        Stream stream,
        ExcelSheetFormatter formatter,
        IEnumerable<(ExcelSheetPage Page, ExcelSheetRow Row)> rows,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken = default)
    {
        var source = formatter.Source;
        using var writer = new StreamWriter(stream, new UTF8Encoding(true), 65536, true);
        writer.NewLine = "\r\n";
        writer.WriteLine(string.Join(',', headers.Select(Escape)));

        var count = 0;
        var values = new string[headers.Count];
        foreach (var (page, row) in rows) {
            cancellationToken.ThrowIfCancellationRequested();
            var i = 0;
            values[i++] = row.RowId.ToString(CultureInfo.InvariantCulture);
            if (source.HasSubrows)
                values[i++] = row.SubrowId.ToString(CultureInfo.InvariantCulture);
            foreach (var column in source.Columns)
                values[i++] = Escape(formatter.GetText(page, row, column.Index));
            writer.WriteLine(string.Join(',', values));
            count++;
        }

        return count;
    }

    /// <summary>Exports a sheet, as the Excel viewer would show it by default (schema names and resolved links, if
    /// a schema is available).</summary>
    /// <param name="vfs">Virtual file system containing <paramref name="file"/>.</param>
    /// <param name="root">Root folder of the game data, used to resolve sheet names and links. May be <c>null</c>.
    /// </param>
    /// <param name="file">An .exh file, to export all rows of the sheet in English (or the first available language
    /// if not available); or an .exd file, to export the rows of that page in its language.</param>
    /// <param name="schemaProvider">Provider of EXDSchema sheet definitions. May be <c>null</c>.</param>
    /// <param name="stream">Stream to write to; left open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="linkResolver">Link resolver to use, so that sheets exported together share the loaded link
    /// targets; it must use the schema set of <paramref name="schemaProvider"/>. If <c>null</c>, one is created.
    /// </param>
    /// <returns>Number of rows written.</returns>
    public static async Task<int> ExportAsync(
        IVirtualFileSystem vfs,
        IVirtualFolder? root,
        IVirtualFile file,
        ExcelSchemaProvider? schemaProvider,
        Stream stream,
        CancellationToken cancellationToken = default,
        ExcelLinkResolver? linkResolver = null)
    {
        var result = await ExcelSheetSource.Open(vfs, root, file, null, cancellationToken);
        var source = result.Source;

        Language language;
        int[] pageIndices;
        if (result.PageIndex is { } pageIndex) {
            language = result.Language ?? Language.None;
            pageIndices = [pageIndex];
        } else {
            var languages = await source.GetAvailableLanguages(cancellationToken);
            if (languages.Length == 0)
                languages = source.DeclaredLanguages.ToArray();
            if (languages.Length == 0)
                languages = [Language.None];
            language = languages.Contains(Language.English) ? Language.English : languages[0];
            pageIndices = Enumerable.Range(0, source.Pages.Count).ToArray();
        }

        ExcelSchemaBinding? binding = null;
        if (schemaProvider is not null) {
            var set = (await schemaProvider.GetSchemaSetAsync().WaitAsync(cancellationToken)).Set;
            binding = ExcelSchemaBinding.Create(source, set);
            if (set is null || !binding.IsMatched)
                linkResolver = null;
            else
                linkResolver ??= new(vfs, root ?? vfs.RootFolder, set, cancellationToken);
        } else {
            linkResolver = null;
        }

        var formatter = new ExcelSheetFormatter(source, binding, linkResolver, language, ExcelCellFormatOptions.None);
        if (formatter.LinkResolver is { } resolver)
            await resolver.PreloadAsync(formatter.GetAllLinkTargets(), language).WaitAsync(cancellationToken);

        var rows = new List<(ExcelSheetPage Page, ExcelSheetRow Row)>();
        foreach (var i in pageIndices) {
            cancellationToken.ThrowIfCancellationRequested();
            if (await source.GetPage(language, i).WaitAsync(cancellationToken) is not { } page)
                continue;
            foreach (var row in page.Rows)
                rows.Add((page, row));
        }

        return Write(stream, formatter, rows, GetHeaders(formatter), cancellationToken);
    }
}
