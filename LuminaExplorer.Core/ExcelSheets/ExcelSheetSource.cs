using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using Lumina.Misc;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Reads an Excel sheet (header and data pages) through an <see cref="IVirtualFileSystem"/>.</summary>
/// <remarks>Data pages are looked up as siblings of the header file, so this works for both SqPack and loose files.
/// </remarks>
public sealed class ExcelSheetSource {
    private readonly IVirtualFileSystem _vfs;
    private readonly IVirtualFolder _folder;
    private readonly ConcurrentDictionary<(Language Language, int PageIndex), Lazy<Task<ExcelSheetPage?>>> _pages =
        new();

    private ExcelSheetSource(
        IVirtualFileSystem vfs,
        IVirtualFolder folder,
        string baseName,
        string sheetName,
        ExcelHeaderFile header)
    {
        this._vfs = vfs;
        this._folder = folder;
        this.BaseName = baseName;
        this.SheetName = sheetName;
        this.Header = header;
        this.Columns = header.ColumnDefinitions.Select((x, i) => new ExcelSheetColumn(i, x)).ToArray();
        this.DeclaredLanguages = header.Languages.Distinct().ToArray();
    }

    /// <summary>Gets the file name of the header without extension (e.g. <c>item</c>).</summary>
    public string BaseName { get; }

    /// <summary>Gets the sheet name, as found in <c>root.exl</c> if available (e.g. <c>Item</c>).</summary>
    public string SheetName { get; }

    public ExcelHeaderFile Header { get; }

    public ExcelSheetColumn[] Columns { get; }

    public ExcelVariant Variant => this.Header.Header.Variant;

    public bool HasSubrows => this.Variant == ExcelVariant.Subrows;

    /// <summary>Gets the size of the fixed size data of a row.</summary>
    public int FixedDataSize => this.Header.Header.DataOffset;

    public IReadOnlyList<ExcelDataPagination> Pages => this.Header.DataPages;

    /// <summary>Gets the languages listed in the header file. Data for some may not exist.</summary>
    public IReadOnlyList<Language> DeclaredLanguages { get; }

    /// <summary>Opens the sheet that a header (.exh) or data page (.exd) file belongs to.</summary>
    /// <param name="vfs">Virtual file system.</param>
    /// <param name="root">Root folder of the game data (used to find <c>exd/root.exl</c>), if any.</param>
    /// <param name="file">The header or data file.</param>
    /// <param name="fileResource">Already loaded file resource of <paramref name="file"/>, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<ExcelSheetOpenResult> Open(
        IVirtualFileSystem vfs,
        IVirtualFolder? root,
        IVirtualFile file,
        FileResource? fileResource,
        CancellationToken cancellationToken = default)
    {
        var exl = await TryLoadRootExl(vfs, root, cancellationToken);

        string folderPath;
        try {
            folderPath = vfs.GetFullPath(file.Parent);
        } catch (Exception) {
            folderPath = string.Empty;
        }

        var fileName = file.Name;
        if (!file.NameResolved) {
            fileName = await ResolveFileName(vfs, file, folderPath, exl, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The name of \"{file.Name}\" is unknown, and it could not be found from exd/root.exl. " +
                    "The sheet name is required to locate the other files of the sheet.");
            vfs.SuggestFullPath(folderPath + fileName);
        }

        string baseName;
        ExcelHeaderFile header;
        int? pageIndex = null;
        Language? language = null;

        if (fileResource is ExcelHeaderFile ||
            (fileResource is null && fileName.EndsWith(ExcelSheetFileNames.HeaderExtension,
                StringComparison.OrdinalIgnoreCase))) {
            baseName = Path.GetFileNameWithoutExtension(fileName);
            header = fileResource as ExcelHeaderFile
                ?? await LoadResource<ExcelHeaderFile>(vfs, file, cancellationToken);
        } else if (ExcelSheetFileNames.TryParseDataFileName(fileName, out baseName, out var startRow,
                       out var lang)) {
            var exhFile = await vfs.LocateFile(file.Parent, ExcelSheetFileNames.GetHeaderFileName(baseName))
                ?? throw new FileNotFoundException(
                    $"Header file \"{ExcelSheetFileNames.GetHeaderFileName(baseName)}\" could not be found.");
            header = await LoadResource<ExcelHeaderFile>(vfs, exhFile, cancellationToken);
            language = lang;
            for (var i = 0; i < header.DataPages.Length; i++) {
                if (header.DataPages[i].StartId == startRow) {
                    pageIndex = i;
                    break;
                }
            }
        } else {
            throw new NotSupportedException($"\"{fileName}\" is not an Excel header or data file.");
        }

        var sheetName = ExcelSheetFileNames.GetSheetNameFromPath(folderPath + fileName) ?? baseName;
        if (exl is not null) {
            var sn = sheetName;
            sheetName = exl.ExdMap.Keys.FirstOrDefault(
                x => string.Equals(x, sn, StringComparison.OrdinalIgnoreCase)) ?? sheetName;
        }

        return new(new(vfs, file.Parent, baseName, sheetName, header), pageIndex, language, folderPath + fileName);
    }

    /// <summary>Opens a sheet by its name.</summary>
    /// <param name="vfs">Virtual file system.</param>
    /// <param name="root">Root folder of the game data, containing <c>exd/</c>.</param>
    /// <param name="sheetName">Name of the sheet, such as <c>Item</c> or <c>quest/000/ClsArc001_00001</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sheet, or <c>null</c> if its header file does not exist.</returns>
    public static async Task<ExcelSheetSource?> OpenByName(
        IVirtualFileSystem vfs,
        IVirtualFolder root,
        string sheetName,
        CancellationToken cancellationToken = default)
    {
        var path = $"exd/{sheetName}{ExcelSheetFileNames.HeaderExtension}";
        if (await vfs.LocateFile(root, path) is not { } file)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var header = await LoadResource<ExcelHeaderFile>(vfs, file, cancellationToken);
        var baseName = sheetName[(sheetName.LastIndexOf('/') + 1)..];
        return new(vfs, file.Parent, baseName, sheetName, header);
    }

    private static async Task<ExcelListFile?> TryLoadRootExl(
        IVirtualFileSystem vfs,
        IVirtualFolder? root,
        CancellationToken cancellationToken)
    {
        if (root is null)
            return null;
        try {
            return await vfs.LocateFile(root, "exd/root.exl") is { } exlFile
                ? await LoadResource<ExcelListFile>(vfs, exlFile, cancellationToken)
                : null;
        } catch (Exception e) when (e is not OperationCanceledException) {
            return null;
        }
    }

    /// <summary>Finds the name of a file with an unknown name, using the sheet names in <c>root.exl</c>.</summary>
    private static async Task<string?> ResolveFileName(
        IVirtualFileSystem vfs,
        IVirtualFile file,
        string folderPath,
        ExcelListFile? exl,
        CancellationToken cancellationToken)
    {
        if (file.NameHash is not { } nameHash || exl is null)
            return null;

        // Sheets that are in the same folder as the file.
        var dir = ExcelSheetFileNames.GetSheetDirectory(
            ExcelSheetFileNames.GetSheetNameFromPath(folderPath + "_" + ExcelSheetFileNames.HeaderExtension) ?? "");
        var candidates = exl.ExdMap.Keys
            .Where(x => string.Equals(ExcelSheetFileNames.GetSheetDirectory(x), dir, StringComparison.OrdinalIgnoreCase))
            .Select(x => x[(x.LastIndexOf('/') + 1)..])
            .ToArray();

        // Header files.
        foreach (var c in candidates) {
            var name = ExcelSheetFileNames.GetHeaderFileName(c);
            if (Hash(name) == nameHash)
                return name;
        }

        // Data files; try the common case of the first page starting at row 0 first.
        var languages = Enum.GetValues<Language>();
        foreach (var c in candidates) {
            foreach (var language in languages) {
                var name = ExcelSheetFileNames.GetDataFileName(c, 0, language);
                if (Hash(name) == nameHash)
                    return name;
            }
        }

        foreach (var c in candidates) {
            cancellationToken.ThrowIfCancellationRequested();
            if (await vfs.LocateFile(file.Parent, ExcelSheetFileNames.GetHeaderFileName(c)) is not { } exhFile)
                continue;

            ExcelHeaderFile header;
            try {
                header = await LoadResource<ExcelHeaderFile>(vfs, exhFile, cancellationToken);
            } catch (Exception e) when (e is not OperationCanceledException) {
                continue;
            }

            foreach (var page in header.DataPages) {
                foreach (var language in header.Languages) {
                    var name = ExcelSheetFileNames.GetDataFileName(c, page.StartId, language);
                    if (Hash(name) == nameHash)
                        return name;
                }
            }
        }

        return null;

        static uint Hash(string name) => Crc32.Get(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
    }

    /// <summary>Finds the languages for which the first data page exists.</summary>
    public async Task<Language[]> GetAvailableLanguages(CancellationToken cancellationToken = default)
    {
        if (this.Pages.Count == 0)
            return this.DeclaredLanguages.ToArray();

        var startId = this.Pages[0].StartId;
        var result = new List<Language>();
        foreach (var language in this.DeclaredLanguages) {
            cancellationToken.ThrowIfCancellationRequested();
            var name = ExcelSheetFileNames.GetDataFileName(this.BaseName, startId, language);
            if (await this._vfs.LocateFile(this._folder, name) is not null)
                result.Add(language);
        }

        return result.ToArray();
    }

    /// <summary>Loads a data page.</summary>
    /// <returns>The page, or <c>null</c> if the file does not exist.</returns>
    public Task<ExcelSheetPage?> GetPage(Language language, int pageIndex) =>
        this._pages.GetOrAdd(
            (language, pageIndex),
            static (key, self) => new(() => Task.Run(() => self.LoadPage(key.Language, key.PageIndex))),
            this).Value;

    /// <summary>Drops cached pages of languages other than the given one.</summary>
    public void TrimCache(Language keepLanguage)
    {
        foreach (var key in this._pages.Keys.Where(x => x.Language != keepLanguage).ToArray())
            this._pages.TryRemove(key, out _);
    }

    private async Task<ExcelSheetPage?> LoadPage(Language language, int pageIndex)
    {
        var name = ExcelSheetFileNames.GetDataFileName(this.BaseName, this.Pages[pageIndex].StartId, language);
        if (await this._vfs.LocateFile(this._folder, name) is not { } file)
            return null;

        var exd = await LoadResource<ExcelDataFile>(this._vfs, file, default);
        return new(pageIndex, language, this.Variant, this.FixedDataSize, exd);
    }

    private static async Task<T> LoadResource<T>(
        IVirtualFileSystem vfs,
        IVirtualFile file,
        CancellationToken cancellationToken) where T : FileResource
    {
        using var lookup = vfs.GetLookup(file);
        return await lookup.AsFileResource<T>(cancellationToken);
    }
}

/// <summary>Result of <see cref="ExcelSheetSource.Open"/>.</summary>
/// <param name="Source">The sheet.</param>
/// <param name="PageIndex">Index of the page, if a data page was opened.</param>
/// <param name="Language">Language of the page, if a data page was opened.</param>
/// <param name="FilePath">Full path of the opened file, with its name resolved if it was unknown.</param>
public sealed record ExcelSheetOpenResult(
    ExcelSheetSource Source,
    int? PageIndex,
    Language? Language,
    string FilePath);
