using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using LuminaExplorer.Core.ExcelSheets;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Excel sheet headers: sheet name, size, languages, and pages.</summary>
public sealed class ExhThumbnailProvider : IThumbnailProvider {
    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<ExcelHeaderFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        var exh = await request.Lookup.AsFileResource<ExcelHeaderFile>(cancellationToken);
        var header = exh.Header;
        var sheetName = (request.FullPath is { } fullPath ? ExcelSheetFileNames.GetSheetNameFromPath(fullPath) : null)
            ?? request.DisplayStem;
        var languages = exh.Languages
            .Distinct()
            .Select(x => x == Language.None ? "neutral" : ExcelSheetFileNames.GetLanguageCode(x))
            .ToArray();

        return new InfoCard("EXH", sheetName)
            .AddLine($"{InfoCard.FormatCount(header.RowCount)} rows × {header.ColumnCount} cols", true)
            .AddLine(header.Variant == ExcelVariant.Subrows ? "with subrows" : null)
            .AddLine(
                exh.DataPages.Length == 1 ? "1 page" : $"{exh.DataPages.Length} pages")
            .AddLine(languages.Length == 0 ? null : string.Join(" ", languages))
            .AddLine($"{header.DataOffset} bytes/row")
            .Render(request.Settings);
    }
}

/// <summary>Excel data pages: sheet name, first row, language, and row count, from the file header only.</summary>
public sealed class ExdThumbnailProvider : IThumbnailProvider {
    private const uint ExdfMagic = 0x46445845;

    public int Priority => 90;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) => request.IsPossibly<ExcelDataFile>();

    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        // EXDF header (big endian): magic, u16 version, u16 unknown, u32 index size, u32 data size.
        var buf = new byte[16];
        await using (var stream = request.Lookup.CreateStream())
            await stream.ReadExactlyAsync(buf, cancellationToken);

        if (BinaryPrimitives.ReadUInt32LittleEndian(buf) != ExdfMagic)
            return null;

        var indexSize = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8));
        var dataSize = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12));
        var rows = indexSize / 8;

        var parsed = ExcelSheetFileNames.TryParseDataFileName(request.Name, out var baseName, out var start, out var lang);
        return new InfoCard("EXD", parsed ? $"{GetSheetName(request, baseName)} p{start}" : request.DisplayStem)
            .AddLine($"{InfoCard.FormatCount(rows)} rows", true)
            .AddLine(
                !parsed ? null
                : lang == Language.None ? "language neutral"
                : ExcelSheetFileNames.GetLanguageDisplayName(lang))
            .AddLine(parsed ? $"from row {start}" : null)
            .AddLine($"{InfoCard.FormatCount(dataSize)} bytes of data")
            .Render(request.Settings);
    }

    private static string GetSheetName(ThumbnailRequest request, string baseName) =>
        request.FullPath is { } fullPath && ExcelSheetFileNames.GetSheetNameFromPath(fullPath) is { } sheetName
            ? sheetName
            : baseName;
}
