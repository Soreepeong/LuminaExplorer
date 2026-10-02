using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.Core.ExcelSheets;

public enum ExcelLinkLookupState {
    /// <summary>The target sheet is being loaded.</summary>
    Pending,

    /// <summary>The target sheet does not exist or could not be loaded.</summary>
    Unavailable,

    /// <summary>The target sheet does not contain the row.</summary>
    NotFound,

    /// <summary>The row has been found.</summary>
    Found,
}

/// <summary>Resolves links to rows of other sheets into their display text. Thread safe.</summary>
/// <remarks>Each target sheet is loaded entirely in the background on first use, and kept as a map of row IDs to
/// the text of their display field (the first subrow, for sheets with subrows).</remarks>
public sealed class ExcelLinkResolver {
    private readonly IVirtualFileSystem _vfs;
    private readonly IVirtualFolder _root;
    private readonly ExcelSchemaSet? _schemaSet;
    private readonly CancellationToken _cancellationToken;
    private readonly ConcurrentDictionary<(string Sheet, Language Language), Lazy<Task<LinkTable?>>> _tables = new();

    public ExcelLinkResolver(
        IVirtualFileSystem vfs,
        IVirtualFolder root,
        ExcelSchemaSet? schemaSet,
        CancellationToken cancellationToken = default)
    {
        this._vfs = vfs;
        this._root = root;
        this._schemaSet = schemaSet;
        this._cancellationToken = cancellationToken;
    }

    /// <summary>Raised from a background thread when a target sheet has finished loading.</summary>
    public event EventHandler? SheetLoaded;

    /// <summary>Looks up the display text of a row; starts loading the sheet if needed.</summary>
    public ExcelLinkLookupState TryGetRowText(string sheetName, Language language, uint rowId, out string text)
    {
        text = string.Empty;
        var task = this.GetTable(sheetName, language);
        if (!task.IsCompleted)
            return ExcelLinkLookupState.Pending;
        if (!task.IsCompletedSuccessfully || task.Result is not { } table)
            return ExcelLinkLookupState.Unavailable;
        if (!table.Texts.TryGetValue(rowId, out var t))
            return ExcelLinkLookupState.NotFound;
        text = t;
        return ExcelLinkLookupState.Found;
    }

    /// <summary>Loads the given sheets.</summary>
    public Task PreloadAsync(IEnumerable<string> sheetNames, Language language) =>
        Task.WhenAll(sheetNames.Distinct().Select(x => this.GetTable(x, language)).ToArray())
            .ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);

    private Task<LinkTable?> GetTable(string sheetName, Language language) =>
        this._tables.GetOrAdd(
            (sheetName, language),
            static (key, self) => new(
                () => Task.Run(
                    async () => {
                        try {
                            return await self.LoadTable(key.Sheet, key.Language);
                        } catch (Exception) {
                            return null;
                        } finally {
                            self.SheetLoaded?.Invoke(self, EventArgs.Empty);
                        }
                    })),
            this).Value;

    private async Task<LinkTable?> LoadTable(string sheetName, Language language)
    {
        var ct = this._cancellationToken;
        var source = await ExcelSheetSource.OpenByName(this._vfs, this._root, sheetName, ct);
        if (source is null)
            return null;

        var languages = await source.GetAvailableLanguages(ct);
        var lang = languages.Contains(language) ? language
            : languages.Contains(Language.None) ? Language.None
            : languages.Contains(Language.English) ? Language.English
            : languages.Length > 0 ? languages[0]
            : Language.None;

        var binding = ExcelSchemaBinding.Create(source, this._schemaSet);
        var displayColumnIndex = binding.FindDisplayColumn(source.Columns);
        var displayColumn = displayColumnIndex == -1 ? null : source.Columns[displayColumnIndex];

        var texts = new Dictionary<uint, string>();
        for (var i = 0; i < source.Pages.Count; i++) {
            ct.ThrowIfCancellationRequested();
            if (await source.GetPage(lang, i) is not { } page)
                continue;
            foreach (var row in page.Rows) {
                if (texts.ContainsKey(row.RowId))
                    continue;
                texts[row.RowId] = displayColumn?.ReadDisplayText(page, row, ExcelCellFormatOptions.TextOnly) ??
                    string.Empty;
            }
        }

        // The source, and the pages cached in it, are dropped here; only the texts are kept.
        return new(texts);
    }

    private sealed record LinkTable(Dictionary<uint, string> Texts);
}
