using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Text.ReadOnly;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window.FileViewers;

/// <summary>Links from cells to the game files and folders that they refer to.</summary>
public partial class ExcelViewer {
    /// <summary>Sheets whose values are names of files in a known folder, without the folder.</summary>
    private static readonly Dictionary<string, string> BareNameFolders = new(StringComparer.OrdinalIgnoreCase) {
        ["VFX"] = "vfx/common/eff/",
        ["Omen"] = "vfx/omen/eff/",
        ["Lockon"] = "vfx/lockon/eff/",
        ["Channeling"] = "vfx/channeling/eff/",
    };

    /// <summary>Words in sheet or field names, and the file extensions that their extensionless paths likely have.
    /// </summary>
    private static readonly (string Keyword, string[] Extensions)[] ExtensionHints = [
        ("vfx", ["avfx"]),
        ("omen", ["avfx"]),
        ("lockon", ["avfx"]),
        ("channeling", ["avfx"]),
        ("bgm", ["scd"]),
        ("music", ["scd"]),
        ("orchestrion", ["scd"]),
        ("sound", ["scd"]),
        ("jingle", ["scd"]),
        ("vibration", ["vib"]),
        ("timeline", ["tmb", "pap"]),
        ("cutscene", ["cutb"]),
        ("territory", ["lvb"]),
        ("exportedsg", ["sgb"]),
        ("sgb", ["sgb"]),
        ("lgb", ["lgb"]),
        ("model", ["mdl"]),
        ("icon", ["tex"]),
        ("image", ["tex"]),
        ("texture", ["tex"]),
    ];

    private static readonly object NoLink = new();

    // Link targets of cells: key is (index into _rows, sheet column); value is a GamePathMatch, or NoLink.
    private readonly Dictionary<(int Row, int Column), object> _linkCache = new();
    private readonly Dictionary<int, string[]> _columnExtensionHints = new();
    private readonly List<ExcelDerivedReference> _derivedScratch = new();

    // Partial paths being resolved in the background.
    private readonly object _linkQueueLock = new();
    private readonly Queue<(string Text, string[] Hints, string Key)> _linkQueue = new();
    private readonly HashSet<string> _linkQueueKeys = new(StringComparer.Ordinal);
    private bool _linkWorkerRunning;
    private int _linkRepaintQueued;

    private GamePathResolver? _pathResolver;
    private ExcelDerivedReferences? _derivedReferences;
    private ExcelDerivedReferencePlan? _derivedPlan;
    private Font? _linkFont;
    private (int Row, int Column) _hoverCell = (-1, -1);

    private ToolStripMenuItem? _showInExplorerMenuItem;
    private ToolStripMenuItem? _openFileMenuItem;
    private ToolStripMenuItem? _copyPathMenuItem;
    private ToolStripSeparator? _linkMenuSeparator;

    /// <summary>Gets or sets the function that shows a game path (folders end with a slash) in the explorer.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? ShowPathInExplorer { get; set; }

    /// <summary>Gets or sets the function that opens the file at a game path in its viewer.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action<string>? OpenPath { get; set; }

    private static Color LinkColor => SystemColors.HotTrack;

    /// <summary>Finds the game file or folder that a cell refers to.</summary>
    /// <param name="viewIndex">Index of the shown row.</param>
    /// <param name="sheetColumn">Index of the column in the sheet.</param>
    /// <param name="resolveNow">Whether to resolve partial paths now, instead of in the background.</param>
    /// <returns>The target, or <c>null</c> if none, or if it is being resolved in the background.</returns>
    public GamePathMatch? GetCellLinkTarget(int viewIndex, int sheetColumn, bool resolveNow = false)
    {
        if (this._source is not { } source ||
            this._pathResolver is not { } resolver ||
            viewIndex < 0 ||
            viewIndex >= this.ShownRowCount ||
            sheetColumn < 0 ||
            sheetColumn >= source.Columns.Length)
            return null;

        var column = source.Columns[sheetColumn];
        var plan = this._derivedPlan;
        var isDerived = plan?.HasColumn(sheetColumn) is true;
        if (!column.IsString && !isDerived)
            return null;

        var rowIndex = this._filter is { } filter ? filter[viewIndex] : viewIndex;
        if (this._linkCache.TryGetValue((rowIndex, sheetColumn), out var cached))
            return cached as GamePathMatch;

        var (page, row) = this._rows[rowIndex];
        GamePathMatch? result;
        if (isDerived) {
            if (this._derivedReferences is not { IsPrepared: true } derived)
                return null;

            this._derivedScratch.Clear();
            derived.Derive(plan!, page, row, this._derivedScratch, sheetColumn);
            result = this._derivedScratch
                .Where(x => x.IsFolder ? resolver.FolderExists(x.Path) : resolver.FileExists(x.Path))
                .Select(x => new GamePathMatch(x.Path, x.IsFolder))
                .FirstOrDefault();
        } else {
            if (column.ReadValue(page, row) is not ReadOnlySeString { IsEmpty: false } s)
                return this.CacheLink(rowIndex, sheetColumn, null);

            string text;
            try {
                text = s.ExtractText();
            } catch (Exception) {
                return this.CacheLink(rowIndex, sheetColumn, null);
            }

            if (GamePathResolver.NormalizeCandidate(text) is null)
                return this.CacheLink(rowIndex, sheetColumn, null);

            var hints = this.GetExtensionHints(sheetColumn);
            result = null;
            foreach (var candidate in this.GetCandidates(source.SheetName, text)) {
                if (resolveNow) {
                    result = resolver.Resolve(candidate, hints);
                } else if (!resolver.TryGetResolved(candidate, hints, out result)) {
                    this.QueueResolve(candidate, hints);
                    return null;
                }

                if (result is not null)
                    break;
            }
        }

        return this.CacheLink(rowIndex, sheetColumn, result);
    }

    private GamePathMatch? CacheLink(int rowIndex, int sheetColumn, GamePathMatch? match)
    {
        this._linkCache[(rowIndex, sheetColumn)] = match ?? NoLink;
        return match;
    }

    private IEnumerable<string> GetCandidates(string sheetName, string text)
    {
        var t = text.Trim();
        if (!t.Contains('/') && BareNameFolders.TryGetValue(sheetName, out var folder))
            yield return folder + t;
        yield return t;
    }

    private string[] GetExtensionHints(int sheetColumn)
    {
        if (this._columnExtensionHints.TryGetValue(sheetColumn, out var hints))
            return hints;

        var names = $"{this._source?.SheetName} {this._binding?.Fields[sheetColumn]?.Name}".ToLowerInvariant();
        hints = ExtensionHints
            .Where(x => names.Contains(x.Keyword, StringComparison.Ordinal))
            .SelectMany(x => x.Extensions)
            .Distinct()
            .ToArray();
        if (string.Equals(this._source?.SheetName, "SE", StringComparison.OrdinalIgnoreCase))
            hints = [..hints, "scd"];
        this._columnExtensionHints[sheetColumn] = hints;
        return hints;
    }

    private void QueueResolve(string text, string[] hints)
    {
        var key = $"{text}|{string.Join(',', hints)}";
        lock (this._linkQueueLock) {
            if (!this._linkQueueKeys.Add(key))
                return;
            this._linkQueue.Enqueue((text, hints, key));
            if (this._linkWorkerRunning)
                return;
            this._linkWorkerRunning = true;
        }

        var resolver = this._pathResolver;
        var cancellationToken = this._closeToken.Token;
        Task.Run(
            () => {
                while (true) {
                    (string Text, string[] Hints, string Key) item;
                    lock (this._linkQueueLock) {
                        if (cancellationToken.IsCancellationRequested || !this._linkQueue.TryDequeue(out item)) {
                            this._linkQueue.Clear();
                            this._linkQueueKeys.Clear();
                            this._linkWorkerRunning = false;
                            return;
                        }
                    }

                    try {
                        resolver?.Resolve(item.Text, item.Hints, cancellationToken);
                    } catch (Exception) {
                        // Shown without a link.
                    }

                    lock (this._linkQueueLock)
                        this._linkQueueKeys.Remove(item.Key);
                    this.QueueLinkRepaint();
                }
            },
            cancellationToken);
    }

    private void QueueLinkRepaint()
    {
        if (Interlocked.Exchange(ref this._linkRepaintQueued, 1) == 1)
            return;

        try {
            this.BeginInvoke(
                () => {
                    this._linkRepaintQueued = 0;
                    if (this.IsDisposed)
                        return;

                    // Cells waiting for resolution were not cached, so a repaint picks up the results.
                    this._grid.Invalidate();
                    this.UpdateLinkCursor();
                });
        } catch (InvalidOperationException) {
            // The window is closing.
            this._linkRepaintQueued = 0;
        }
    }

    private void InitializeLinks()
    {
        this._grid.CellFormatting += this.GridOnCellFormattingForLinks;
        this._grid.CellMouseEnter += this.GridOnCellMouseEnterForLinks;
        this._grid.CellMouseLeave += this.GridOnCellMouseLeaveForLinks;
        this._grid.CellMouseDown += this.GridOnCellMouseDownForLinks;
        this._grid.CellMouseClick += this.GridOnCellMouseClickForLinks;
        this._grid.KeyDown += (_, _) => this.UpdateLinkCursor();
        this._grid.KeyUp += (_, _) => this.UpdateLinkCursor();
        this._grid.ContextMenuStrip!.Opening += this.ContextMenuOnOpeningForLinks;
    }

    private void SetUpLinks(IVirtualFileSystem vfs)
    {
        this._pathResolver = vfs is SqpackFileSystem sqfs ? GamePathResolver.Get(sqfs) : null;
        this._derivedReferences = this._pathResolver is { } resolver ? ExcelDerivedReferences.Get(resolver) : null;

        // Icons, models, and ModelChara links are shown as links once the sheets needed for them are loaded.
        if (this._derivedReferences is { IsPrepared: false } derived) {
            Task.Run(derived.Prepare, this._closeToken.Token)
                .ContinueWith(
                    _ => {
                        if (this.IsDisposed)
                            return;
                        this._linkCache.Clear();
                        this._grid.Invalidate();
                    },
                    this._closeToken.Token,
                    TaskContinuationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void ResetLinks()
    {
        this._linkCache.Clear();
        this._columnExtensionHints.Clear();
        this._derivedPlan = null;
        this._hoverCell = (-1, -1);
    }

    private void OnSchemaBoundForLinks(ExcelSheetSource source, ExcelSchemaBinding binding)
    {
        this._derivedPlan = this._derivedReferences?.CreatePlan(source, binding);
        this._columnExtensionHints.Clear();
        this._linkCache.Clear();
        this._grid.Invalidate();
    }

    private GamePathMatch? GetGridCellLink(int rowIndex, int gridColumn, bool resolveNow = false) =>
        gridColumn < this._firstDataColumn
            ? null
            : this.GetCellLinkTarget(rowIndex, gridColumn - this._firstDataColumn, resolveNow);

    private string? GetLinkToolTip(int rowIndex, int gridColumn)
    {
        if (this.GetGridCellLink(rowIndex, gridColumn) is not { } link)
            return null;
        return this.ShowPathInExplorer is null
            ? $"→ {link.Path}"
            : $"→ {link.Path}\nCtrl+click to show in Explorer; right-click for more";
    }

    private void GridOnCellFormattingForLinks(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= this.ShownRowCount || e.CellStyle is not { } style)
            return;
        if (this.GetGridCellLink(e.RowIndex, e.ColumnIndex) is null)
            return;

        style.ForeColor = LinkColor;
        if (this._hoverCell == (e.RowIndex, e.ColumnIndex)) {
            var font = style.Font ?? this._grid.Font;
            if (this._linkFont is null || !Equals(this._linkFont.FontFamily, font.FontFamily) ||
                Math.Abs(this._linkFont.Size - font.Size) > 0.01f) {
                this._linkFont?.Dispose();
                this._linkFont = new(font, font.Style | FontStyle.Underline);
            }

            style.Font = this._linkFont;
        }
    }

    private void GridOnCellMouseEnterForLinks(object? sender, DataGridViewCellEventArgs e)
    {
        var previous = this._hoverCell;
        this._hoverCell = (e.RowIndex, e.ColumnIndex);
        this.InvalidateGridCell(previous);
        this.InvalidateGridCell(this._hoverCell);
        this.UpdateLinkCursor();
    }

    private void GridOnCellMouseLeaveForLinks(object? sender, DataGridViewCellEventArgs e)
    {
        var previous = this._hoverCell;
        this._hoverCell = (-1, -1);
        this.InvalidateGridCell(previous);
        this.UpdateLinkCursor();
    }

    private void InvalidateGridCell((int Row, int Column) cell)
    {
        if (cell.Row >= 0 && cell.Row < this._grid.RowCount && cell.Column >= 0 &&
            cell.Column < this._grid.ColumnCount)
            this._grid.InvalidateCell(cell.Column, cell.Row);
    }

    /// <summary>Shows the hand cursor over links while Ctrl is held.</summary>
    private void UpdateLinkCursor()
    {
        var overLink = this._hoverCell.Row >= 0 &&
            (ModifierKeys & Keys.Control) != 0 &&
            this.ShowPathInExplorer is not null &&
            this.GetGridCellLink(this._hoverCell.Row, this._hoverCell.Column) is not null;
        var cursor = overLink ? Cursors.Hand : Cursors.Default;
        if (this._grid.Cursor != cursor)
            this._grid.Cursor = cursor;
    }

    private void GridOnCellMouseDownForLinks(object? sender, DataGridViewCellMouseEventArgs e)
    {
        // Right-clicking a cell makes it the current one, so that the context menu is about it.
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var cell = this._grid[e.ColumnIndex, e.RowIndex];
        if (cell.Selected)
            return;

        try {
            this._grid.ClearSelection();
            this._grid.CurrentCell = cell;
        } catch (InvalidOperationException) {
            // ignore
        }
    }

    private void GridOnCellMouseClickForLinks(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || (ModifierKeys & Keys.Control) == 0 || e.RowIndex < 0)
            return;
        if (this.ShowPathInExplorer is not { } show ||
            this.GetGridCellLink(e.RowIndex, e.ColumnIndex, true) is not { } link)
            return;

        show(link.Path);
    }

    private void AddLinkMenuItems(ContextMenuStrip menu)
    {
        this._showInExplorerMenuItem = new(
            "Show in &Explorer",
            null,
            (_, _) => {
                if (this.GetCurrentCellLink() is { } link)
                    this.ShowPathInExplorer?.Invoke(link.Path);
            }) {
            ShortcutKeyDisplayString = "Ctrl+Click",
        };
        this._openFileMenuItem = new(
            "&Open file",
            null,
            (_, _) => {
                if (this.GetCurrentCellLink() is { IsFolder: false } link)
                    this.OpenPath?.Invoke(link.Path);
            });
        this._copyPathMenuItem = new(
            "Copy &path",
            null,
            (_, _) => {
                if (this.GetCurrentCellLink() is not { } link)
                    return;
                try {
                    Clipboard.SetText(link.Path);
                    this._progressStatus.Text = $"Copied \"{link.Path}\".";
                } catch (Exception e) {
                    this._progressStatus.Text = $"Copy failed: {e.Message}";
                }
            });
        this._linkMenuSeparator = new();
        menu.Items.AddRange(
            [this._showInExplorerMenuItem, this._openFileMenuItem, this._copyPathMenuItem, this._linkMenuSeparator]);
    }

    private GamePathMatch? GetCurrentCellLink() =>
        this._grid.CurrentCell is { RowIndex: >= 0 } cell && cell.RowIndex < this.ShownRowCount
            ? this.GetGridCellLink(cell.RowIndex, cell.ColumnIndex, true)
            : null;

    private void ContextMenuOnOpeningForLinks(object? sender, CancelEventArgs e)
    {
        var link = this.GetCurrentCellLink();
        var visible = link is not null;
        this._showInExplorerMenuItem!.Visible = visible && this.ShowPathInExplorer is not null;
        this._openFileMenuItem!.Visible = visible && link?.IsFolder is false && this.OpenPath is not null;
        this._copyPathMenuItem!.Visible = visible;
        this._linkMenuSeparator!.Visible = visible;
        if (link is not null) {
            this._showInExplorerMenuItem.ToolTipText = this._copyPathMenuItem.ToolTipText = link.Path;
            this._openFileMenuItem.ToolTipText = link.Path;
        }
    }
}
