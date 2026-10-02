using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window.FileViewers;

/// <summary>Shows an Excel sheet (.exh with its .exd pages) in a virtual grid.</summary>
public partial class ExcelViewer : Form {
    private const int MinimumDefaultWidth = 1280;
    private const int MinimumDefaultHeight = 800;
    private const int ColumnWidthSampleRows = 16;

    private static readonly Guid ExcelViewerSaveToGuid = Guid.Parse("0f5d1c1e-3f7a-4a39-9d55-3c8f0f0b6e2a");

    private readonly CancellationTokenSource _closeToken = new();

    private readonly ToolStrip _toolStrip;
    private readonly ToolStripLabel _languageLabel;
    private readonly ToolStripComboBox _languageCombo;
    private readonly ToolStripTextBox _filterBox;
    private readonly ToolStripTextBox _gotoBox;
    private readonly ToolStripButton _textOnlyButton;
    private readonly ToolStripButton _hexButton;
    private readonly ToolStripButton _schemaButton;
    private readonly ToolStripButton _resolveLinksButton;
    private readonly ToolStripButton _exportButton;
    private readonly BufferedDataGridView _grid;

    // Sheet column to select on the next scroll to a row; see GoToCell.
    private int? _pendingGotoColumn;
    private readonly Label _messageLabel;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _sheetStatus;
    private readonly ToolStripStatusLabel _schemaStatus;
    private readonly ToolStripStatusLabel _rowsStatus;
    private readonly ToolStripStatusLabel _progressStatus;
    private readonly ToolStripStatusLabel _selectionStatus;
    private readonly System.Windows.Forms.Timer _filterTimer;

    private readonly List<(ExcelSheetPage Page, ExcelSheetRow Row)> _rows = new();

    private CancellationTokenSource _openCancel = new();
    private CancellationTokenSource _loadCancel = new();
    private CancellationTokenSource _filterCancel = new();

    private IVirtualFileSystem? _vfs;
    private IVirtualFolder? _root;
    private ExcelSheetSource? _source;
    private ExcelSchemaLoadResult? _schemaLoadResult;
    private ExcelSchemaBinding? _binding;
    private ExcelLinkResolver? _linkResolver;
    private ExcelSheetFormatter? _formatter;
    private int _invalidateQueued;
    private Language _language;
    private int _firstDataColumn;
    private int[]? _filter;
    private string _appliedFilter = string.Empty;
    private ExcelCellFormatOptions _formatOptions;
    private (uint RowId, ushort SubrowId)? _pendingGoto;
    private Task _loadTask = Task.CompletedTask;
    private bool _loadComplete;
    private bool _columnWidthsInitialized;
    private bool _suppressLanguageChange;

    public ExcelViewer()
    {
        this.SuspendLayout();

        this.Text = "Excel Viewer";
        this.KeyPreview = false;
        this.StartPosition = FormStartPosition.Manual;

        this._grid = new() {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ShowCellToolTips = true,
            ShowEditingIcon = false,
            StandardTab = true,
            BorderStyle = BorderStyle.None,
            BackgroundColor = SystemColors.Window,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable,
        };
        this._grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        this._grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        this._grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        this._grid.CellValueNeeded += this.GridOnCellValueNeeded;
        this._grid.CurrentCellChanged += this.GridOnCurrentCellChanged;
        this._grid.CopyRequested += this.GridOnCopyRequested;
        this._grid.CellPainting += this.GridOnCellPainting;
        this._grid.CellToolTipTextNeeded += this.GridOnCellToolTipTextNeeded;
        this._grid.ContextMenuStrip = this.CreateGridContextMenu();
        this.InitializeLinks();

        this._messageLabel = new() {
            Dock = DockStyle.Fill,
            Visible = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.GrayText,
        };

        this._languageLabel = new("Language:") { Visible = false };
        this._languageCombo = new() {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AutoSize = false,
            Width = this.LogicalToDeviceUnits(180),
        };
        this._languageCombo.SelectedIndexChanged += this.LanguageComboOnSelectedIndexChanged;

        this._filterBox = new() {
            AutoSize = false,
            Width = this.LogicalToDeviceUnits(240),
            ToolTipText = "Show only rows containing this text in any cell (Ctrl+F)",
        };
        this._filterBox.TextBox.PlaceholderText = "Filter rows (Ctrl+F)";
        this._filterBox.TextChanged += this.FilterBoxOnTextChanged;
        this._filterBox.KeyDown += this.FilterBoxOnKeyDown;

        this._gotoBox = new() {
            AutoSize = false,
            Width = this.LogicalToDeviceUnits(120),
            ToolTipText = "Go to row ID; use 123.4 for subrows (Ctrl+G)",
        };
        this._gotoBox.TextBox.PlaceholderText = "Go to row (Ctrl+G)";
        this._gotoBox.KeyDown += this.GotoBoxOnKeyDown;

        this._textOnlyButton = new("Text only") {
            CheckOnClick = true,
            ToolTipText = "Show only the text of strings, without SeString macros",
        };
        this._textOnlyButton.CheckedChanged += this.FormatButtonOnCheckedChanged;

        this._hexButton = new("Hex") {
            CheckOnClick = true,
            ToolTipText = "Show integers in hexadecimal",
        };
        this._hexButton.CheckedChanged += this.FormatButtonOnCheckedChanged;

        this._schemaButton = new("Schema") {
            CheckOnClick = true,
            Checked = true,
            Enabled = false,
            ToolTipText = "Name and format columns using the sheet definitions from EXDSchema",
        };
        this._schemaButton.CheckedChanged += this.FormatButtonOnCheckedChanged;

        this._resolveLinksButton = new("Resolve links") {
            CheckOnClick = true,
            Checked = true,
            Enabled = false,
            ToolTipText = "Show the display field of the rows that link columns point to",
        };
        this._resolveLinksButton.CheckedChanged += this.FormatButtonOnCheckedChanged;

        this._exportButton = new("Export CSV...") {
            ToolTipText = "Export the shown rows to a CSV file",
            Enabled = false,
        };
        this._exportButton.Click += (_, _) => this.ExportCsv();

        this._toolStrip = new() {
            GripStyle = ToolStripGripStyle.Hidden,
            Dock = DockStyle.Top,
        };
        this._toolStrip.Items.AddRange(
            [
                this._languageLabel,
                this._languageCombo,
                new ToolStripSeparator(),
                this._filterBox,
                this._gotoBox,
                new ToolStripSeparator(),
                this._textOnlyButton,
                this._hexButton,
                new ToolStripSeparator(),
                this._schemaButton,
                this._resolveLinksButton,
                new ToolStripSeparator(),
                this._exportButton,
            ]);

        this._sheetStatus = new();
        this._schemaStatus = new();
        this._rowsStatus = new();
        this._progressStatus = new();
        this._selectionStatus = new();
        this._statusStrip = new() {
            Dock = DockStyle.Bottom,
        };
        this._statusStrip.Items.AddRange(
            [this._sheetStatus, this._schemaStatus, this._rowsStatus, this._selectionStatus, this._progressStatus]);

        this._filterTimer = new() { Interval = 400 };
        this._filterTimer.Tick += (_, _) => {
            this._filterTimer.Stop();
            this.ApplyFilter();
        };

        this.Controls.Add(this._grid);
        this.Controls.Add(this._messageLabel);
        this.Controls.Add(this._toolStrip);
        this.Controls.Add(this._statusStrip);

        this.ResumeLayout(true);
    }

    /// <summary>Gets the opened sheet, if loaded.</summary>
    public ExcelSheetSource? Source => this._source;

    /// <summary>Gets the number of rows currently shown (after filtering).</summary>
    public int ShownRowCount => this._filter?.Length ?? this._rows.Count;

    /// <summary>Gets a task that completes when all pages of the current language have been loaded.</summary>
    public Task LoadTask => this._loadTask;

    /// <summary>Tests if this viewer can show the given file resource.</summary>
    public static bool MaySupportFileResource(FileResource? fileResource) =>
        fileResource is ExcelHeaderFile or ExcelDataFile;

    /// <summary>Opens the sheet that a header (.exh) or data page (.exd) file belongs to.</summary>
    /// <param name="vfs">Virtual file system containing <paramref name="file"/>.</param>
    /// <param name="root">Root folder of the game data, used to resolve the proper sheet name from
    /// <c>exd/root.exl</c>. May be <c>null</c>.</param>
    /// <param name="file">An .exh or .exd file. Data pages are looked up next to it.</param>
    /// <param name="fileResource">Already loaded resource of <paramref name="file"/>, if available.</param>
    /// <param name="schemaProvider">Provider of EXDSchema sheet definitions, used to name and format columns.
    /// May be <c>null</c>.</param>
    /// <remarks>
    /// Opening an .exd file shows the whole sheet in the language of the page, scrolled to the first row of the page.
    /// </remarks>
    public void SetFile(
        IVirtualFileSystem vfs,
        IVirtualFolder? root,
        IVirtualFile file,
        FileResource? fileResource,
        ExcelSchemaProvider? schemaProvider = null)
    {
        this._openCancel.Cancel();
        this._loadCancel.Cancel();
        this._filterCancel.Cancel();
        var cts = this._openCancel = CancellationTokenSource.CreateLinkedTokenSource(this._closeToken.Token);

        string fullPath;
        try {
            fullPath = vfs.GetFullPath(file);
        } catch (Exception) {
            fullPath = file.Name;
        }

        this.Text = fullPath;
        this.ResetSheet();
        this._vfs = vfs;
        this._root = root ?? vfs.RootFolder;
        this.SetUpLinks(vfs);
        this._sheetStatus.Text = "Opening...";

        if (schemaProvider is null) {
            this._schemaStatus.Text = "No schema";
        } else {
            this._schemaStatus.Text = "Schema: loading...";
            schemaProvider.GetSchemaSetAsync()
                .ContinueWith(
                    r => {
                        if (cts.IsCancellationRequested || this.IsDisposed)
                            return;
                        this._schemaLoadResult = r.IsCompletedSuccessfully
                            ? r.Result
                            : new(null, r.Exception?.InnerException?.Message ?? "Failed to load the schema.");
                        this.BindSchema();
                    },
                    cts.Token,
                    TaskContinuationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        Task.Run(
                async () => {
                    var result = await ExcelSheetSource.Open(vfs, root, file, fileResource, cts.Token);
                    var languages = await result.Source.GetAvailableLanguages(cts.Token);
                    return (result, languages);
                },
                cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || this.IsDisposed)
                        return;

                    if (!r.IsCompletedSuccessfully) {
                        var e = r.Exception?.InnerException ?? r.Exception;
                        this._sheetStatus.Text = "Failed to open";
                        this.ShowMessage($"Failed to open \"{fullPath}\".\n\n{e?.Message}");
                        return;
                    }

                    var (result, languages) = r.Result;
                    this.SetSource(result, languages);
                },
                cts.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Shows this window around the center of the given control.</summary>
    public void ShowRelativeTo(Control opener)
    {
        var screen = Screen.FromControl(opener);
        var workingArea = screen.WorkingArea;
        var size = this.LogicalToDeviceUnits(new Size(MinimumDefaultWidth, MinimumDefaultHeight));
        size = new(Math.Min(size.Width, workingArea.Width), Math.Min(size.Height, workingArea.Height));

        var center = opener.PointToScreen(new(opener.Width / 2, opener.Height / 2));
        var x = Math.Min(Math.Max(center.X - size.Width / 2, workingArea.Left), workingArea.Right - size.Width);
        var y = Math.Min(Math.Max(center.Y - size.Height / 2, workingArea.Top), workingArea.Bottom - size.Height);

        this.SetBounds(x, y, size.Width, size.Height);
        this.Show();
    }

    /// <summary>
    /// Scrolls to the given row like <see cref="GoToRow"/>, and selects the cell of the given sheet column.
    /// </summary>
    /// <returns><c>true</c> if an exact match was found.</returns>
    public bool GoToCell(uint rowId, ushort subrowId, int columnIndex)
    {
        this._pendingGotoColumn = columnIndex;
        return this.GoToRow(rowId, subrowId);
    }

    /// <summary>Scrolls to the given row, or the nearest following one if it does not exist.</summary>
    /// <returns><c>true</c> if an exact match was found.</returns>
    public bool GoToRow(uint rowId, ushort subrowId = 0)
    {
        var count = this.ShownRowCount;
        if (count == 0) {
            this._pendingGoto = (rowId, subrowId);
            return false;
        }

        var index = this.FindViewIndex(rowId, subrowId);
        if (index >= count) {
            if (!this._loadComplete) {
                this._pendingGoto = (rowId, subrowId);
                return false;
            }

            index = count - 1;
        }

        this._pendingGoto = null;
        this.ScrollToViewIndex(index);
        var row = this.RowAt(index).Row;
        return row.RowId == rowId && row.SubrowId == subrowId;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData) {
            case Keys.Escape:
                this.Close();
                return true;
            case Keys.Control | Keys.F:
                this._filterBox.Focus();
                this._filterBox.SelectAll();
                return true;
            case Keys.Control | Keys.G:
                this._gotoBox.Focus();
                this._gotoBox.SelectAll();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this._closeToken.Cancel();
        this._openCancel.Cancel();
        this._loadCancel.Cancel();
        this._filterCancel.Cancel();
        this._filterTimer.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._closeToken.Cancel();
            this._filterTimer.Dispose();
            this._linkFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowMessage(string? message)
    {
        this._messageLabel.Text = message ?? string.Empty;
        this._messageLabel.Visible = message is not null;
        this._grid.Visible = message is null;
    }

    private void ResetSheet()
    {
        this._source = null;
        this._schemaLoadResult = null;
        this._binding = null;
        if (this._linkResolver is not null)
            this._linkResolver.SheetLoaded -= this.LinkResolverOnSheetLoaded;
        this._linkResolver = null;
        this._formatter = null;
        this._schemaButton.Enabled = this._resolveLinksButton.Enabled = false;
        this._rows.Clear();
        this._filter = null;
        this._appliedFilter = string.Empty;
        this._pendingGoto = null;
        this._loadComplete = false;
        this._columnWidthsInitialized = false;
        this._grid.RowCount = 0;
        this._grid.Columns.Clear();
        this._exportButton.Enabled = false;
        this._languageLabel.Visible = this._languageCombo.Visible = false;
        this._sheetStatus.Text = this._rowsStatus.Text = this._progressStatus.Text = string.Empty;
        this._schemaStatus.Text = string.Empty;
        this._selectionStatus.Text = string.Empty;
        this.ResetLinks();
        this.ShowMessage(null);
    }

    private void SetSource(ExcelSheetOpenResult result, Language[] availableLanguages)
    {
        var source = this._source = result.Source;
        this.Text = $"{source.SheetName} - {result.FilePath}";
        this._sheetStatus.Text =
            $"{source.SheetName} | {source.Variant} | {source.Columns.Length} columns | " +
            $"{source.Pages.Count} pages | row size 0x{source.FixedDataSize:X}";

        this.UpdateFormatter();
        this.CreateColumns(source);
        this.UpdateColumnHeaders();
        this.BindSchema();

        if (availableLanguages.Length == 0)
            availableLanguages = source.DeclaredLanguages.ToArray();
        if (availableLanguages.Length == 0)
            availableLanguages = [Language.None];

        var language =
            result.Language is { } l && availableLanguages.Contains(l) ? l
            : availableLanguages.Contains(Language.English) ? Language.English
            : availableLanguages[0];

        this._suppressLanguageChange = true;
        try {
            this._languageCombo.Items.Clear();
            foreach (var lang in availableLanguages)
                this._languageCombo.Items.Add(new LanguageItem(lang));
            this._languageCombo.SelectedIndex = Array.IndexOf(availableLanguages, language);
            this._languageLabel.Visible = this._languageCombo.Visible = availableLanguages.Length > 1 ||
                availableLanguages[0] != Language.None;
            this._languageCombo.Enabled = availableLanguages.Length > 1;
        } finally {
            this._suppressLanguageChange = false;
        }

        if (result.PageIndex is { } pageIndex)
            this._pendingGoto = (source.Pages[pageIndex].StartId, 0);

        this._exportButton.Enabled = true;
        this.StartLoading(language);
    }

    private void CreateColumns(ExcelSheetSource source)
    {
        var columns = new List<DataGridViewColumn>();
        var rowIdStyle = new DataGridViewCellStyle {
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText,
            Alignment = DataGridViewContentAlignment.MiddleRight,
        };

        columns.Add(
            new DataGridViewTextBoxColumn {
                Name = "RowId",
                HeaderText = "RowId",
                ToolTipText = "Row ID",
                Frozen = true,
                DefaultCellStyle = rowIdStyle,
            });

        if (source.HasSubrows) {
            columns.Add(
                new DataGridViewTextBoxColumn {
                    Name = "SubrowId",
                    HeaderText = "Subrow",
                    ToolTipText = "Subrow ID",
                    Frozen = true,
                    DefaultCellStyle = rowIdStyle,
                });
        }

        this._firstDataColumn = columns.Count;

        foreach (var column in source.Columns) {
            var c = new DataGridViewTextBoxColumn {
                Name = $"C{column.Index}",
                HeaderText = $"{column.Name}\n{column.ShortTypeName} {column.LocationText}",
                ToolTipText = column.Description,
            };
            if (column.IsNumeric)
                c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            columns.Add(c);
        }

        foreach (var c in columns) {
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.ReadOnly = true;
            // The sum of FillWeight may not exceed 65535; some sheets have more than a thousand columns.
            c.FillWeight = 1;
            c.Width = this.LogicalToDeviceUnits(c.Index < this._firstDataColumn ? 64 : 80);
        }

        this._grid.ColumnHeadersHeight = this._grid.Font.Height * 2 + this.LogicalToDeviceUnits(10);
        this._grid.Columns.AddRange(columns.ToArray());
    }

    /// <summary>Associates the columns with the schema, once both the sheet and the schema set are available.
    /// </summary>
    private void BindSchema()
    {
        if (this._source is not { } source || this._schemaLoadResult is not { } loadResult)
            return;

        var cts = this._openCancel;
        var set = loadResult.Set;
        Task.Run(() => ExcelSchemaBinding.Create(source, set), cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || this.IsDisposed || this._source != source)
                        return;

                    if (!r.IsCompletedSuccessfully) {
                        this._schemaStatus.Text =
                            $"Schema failed: {r.Exception?.InnerException?.Message ?? r.Exception?.Message}";
                        return;
                    }

                    var binding = this._binding = r.Result;
                    this.OnSchemaBoundForLinks(source, binding);
                    if (set is not null && this._vfs is { } vfs && this._root is { } root) {
                        this._linkResolver = new(vfs, root, set, this._closeToken.Token);
                        this._linkResolver.SheetLoaded += this.LinkResolverOnSheetLoaded;
                    }

                    this._schemaStatus.Text = binding.Status == ExcelSchemaBindingStatus.NoSchemaSet
                        ? $"No schema: {loadResult.Message}"
                        : loadResult.Message is { } note
                            ? $"{binding.Message} ({note})"
                            : binding.Message;
                    this._schemaStatus.ToolTipText = this._schemaStatus.Text;
                    this._schemaButton.Enabled = binding.IsMatched;

                    this.UpdateFormatter();
                    this.UpdateColumnHeaders();
                    if (this._appliedFilter != string.Empty)
                        this.ApplyFilter(true);
                },
                cts.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Creates the formatter for the current sheet, language, and options.</summary>
    private void UpdateFormatter()
    {
        this._formatOptions =
            (this._textOnlyButton.Checked ? ExcelCellFormatOptions.TextOnly : ExcelCellFormatOptions.None) |
            (this._hexButton.Checked ? ExcelCellFormatOptions.HexIntegers : ExcelCellFormatOptions.None);

        if (this._source is not { } source) {
            this._formatter = null;
            return;
        }

        var useSchema = this._schemaButton.Checked && this._binding is { IsMatched: true };
        this._resolveLinksButton.Enabled = useSchema && this._linkResolver is not null;
        this._formatter = new(
            source,
            useSchema ? this._binding : null,
            useSchema && this._resolveLinksButton.Checked ? this._linkResolver : null,
            this._language,
            this._formatOptions);
        this._grid.Invalidate();
    }

    /// <summary>Updates the column headers and styles to the field names, if the schema is in use.</summary>
    private void UpdateColumnHeaders()
    {
        if (this._source is not { } source || this._formatter is not { } formatter)
            return;

        var swatchPadding = new Padding(this.LogicalToDeviceUnits(28), 0, 0, 0);
        foreach (var column in source.Columns) {
            var c = this._grid.Columns[this._firstDataColumn + column.Index];
            var field = formatter.GetField(column.Index);
            column.Name = formatter.GetColumnName(column.Index);
            if (field is null) {
                c.HeaderText = $"{column.Name}\n{column.ShortTypeName} {column.LocationText}";
                c.ToolTipText = column.Description;
            } else {
                c.HeaderText = $"{field.Name}\n#{column.Index} {column.ShortTypeName} {column.LocationText}";
                c.ToolTipText = $"{field.Description}\n\n{column.Description}";
            }

            c.DefaultCellStyle.Alignment = column.IsNumeric && !formatter.MayHaveFormattedText(column.Index)
                ? DataGridViewContentAlignment.MiddleRight
                : DataGridViewContentAlignment.MiddleLeft;
            c.DefaultCellStyle.Padding = field?.Kind == ExcelSchemaFieldKind.Color ? swatchPadding : Padding.Empty;
        }

        this._columnWidthsInitialized = false;
        this.InitializeColumnWidths();
        this.GridOnCurrentCellChanged(null, EventArgs.Empty);
    }

    private void LinkResolverOnSheetLoaded(object? sender, EventArgs e)
    {
        // Raised from background threads; coalesce repaints.
        if (Interlocked.Exchange(ref this._invalidateQueued, 1) == 1)
            return;

        try {
            this.BeginInvoke(
                () => {
                    this._invalidateQueued = 0;
                    if (this.IsDisposed || sender != this._linkResolver)
                        return;
                    this._grid.Invalidate();
                    this.GridOnCurrentCellChanged(null, EventArgs.Empty);
                });
        } catch (InvalidOperationException) {
            // The window is closing.
            this._invalidateQueued = 0;
        }
    }

    private void StartLoading(Language language)
    {
        if (this._source is not { } source)
            return;

        this._loadCancel.Cancel();
        this._filterCancel.Cancel();
        var cts = this._loadCancel = CancellationTokenSource.CreateLinkedTokenSource(this._closeToken.Token);

        this._language = language;
        source.TrimCache(language);
        this.UpdateFormatter();

        this._rows.Clear();
        this._filter = null;
        this._appliedFilter = string.Empty;
        this._loadComplete = false;
        this._grid.RowCount = 0;
        this._linkCache.Clear();
        this.UpdateRowsStatus();

        this._loadTask = this.LoadPages(source, language, cts.Token);

        if (this._filterBox.Text.Trim() != string.Empty)
            this.ApplyFilter();
    }

    /// <summary>Loads all pages in order; runs on the UI thread, while the actual loading is done in the background.
    /// </summary>
    private async Task LoadPages(ExcelSheetSource source, Language language, CancellationToken cancellationToken)
    {
        var pageCount = source.Pages.Count;
        var tasks = new Task<ExcelSheetPage?>[pageCount];
        for (var i = 0; i < pageCount; i++)
            tasks[i] = source.GetPage(language, i);

        var missing = 0;
        var failed = 0;
        Exception? firstError = null;
        var nextUpdate = 0L;

        for (var i = 0; i < pageCount; i++) {
            ExcelSheetPage? page;
            var pageFailed = false;
            try {
                page = await tasks[i];
            } catch (Exception e) {
                firstError ??= e;
                failed++;
                pageFailed = true;
                page = null;
            }

            if (cancellationToken.IsCancellationRequested || this.IsDisposed)
                return;

            if (page is null) {
                if (!pageFailed)
                    missing++;
            } else {
                foreach (var row in page.Rows)
                    this._rows.Add((page, row));
            }

            this._progressStatus.Text = $"Loading pages {i + 1}/{pageCount}...";

            if (Environment.TickCount64 >= nextUpdate || i == pageCount - 1) {
                this.SyncRowCount();
                this.InitializeColumnWidths();
                if (this._pendingGoto is { } pg)
                    this.GoToRow(pg.RowId, pg.SubrowId);
                nextUpdate = Environment.TickCount64 + 100;
            }
        }

        this._loadComplete = true;
        this.SyncRowCount();
        this.InitializeColumnWidths();
        if (this._pendingGoto is { } pendingGoto)
            this.GoToRow(pendingGoto.RowId, pendingGoto.SubrowId);
        this.GridOnCurrentCellChanged(null, EventArgs.Empty);

        var status = new StringBuilder();
        if (missing > 0)
            status.Append(CultureInfo.InvariantCulture, $"{missing} page(s) missing. ");
        if (failed > 0)
            status.Append(CultureInfo.InvariantCulture, $"{failed} page(s) failed: {firstError?.Message}");
        this._progressStatus.Text = status.ToString().Trim();

        if (this._rows.Count == 0 && pageCount > 0)
            this.ShowMessage(failed > 0 ? $"Failed to load the sheet.\n\n{firstError}" : "No rows.");
    }

    private void SyncRowCount()
    {
        if (this._filter is null && this._grid.RowCount != this._rows.Count)
            this._grid.RowCount = this._rows.Count;
        this.UpdateRowsStatus();
    }

    private void UpdateRowsStatus()
    {
        this._rowsStatus.Text = this._filter is null
            ? $"{this._rows.Count:N0} rows"
            : $"{this._filter.Length:N0} of {this._rows.Count:N0} rows match \"{this._appliedFilter}\"";
    }

    private void InitializeColumnWidths()
    {
        if (this._columnWidthsInitialized || this._formatter is not { } formatter || this._rows.Count == 0)
            return;
        this._columnWidthsInitialized = true;

        var font = this._grid.DefaultCellStyle.Font ?? this._grid.Font;
        var headerFont = this._grid.ColumnHeadersDefaultCellStyle.Font ?? font;
        var padding = this.LogicalToDeviceUnits(12);
        var minWidth = this.LogicalToDeviceUnits(36);
        var maxWidth = this.LogicalToDeviceUnits(360);
        var sampleCount = Math.Min(ColumnWidthSampleRows, this._rows.Count);
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var linkMinWidth = this.LogicalToDeviceUnits(160);
        var modelIdMinWidth = TextRenderer.MeasureText("w0000b0000 v00", font, Size.Empty, flags).Width;

        using var redraw = new ControlExtensions.ScopedDisableRedraw(this._grid);
        for (var col = 0; col < this._grid.Columns.Count; col++) {
            var column = this._grid.Columns[col];
            var width = 0;
            foreach (var line in column.HeaderText.Split('\n'))
                width = Math.Max(width, TextRenderer.MeasureText(line, headerFont, Size.Empty, flags).Width);

            for (var i = 0; i < sampleCount; i++) {
                var text = this.GetCellText(i, col, formatter);
                if (text.Length > 0)
                    width = Math.Max(width, TextRenderer.MeasureText(text, font, Size.Empty, flags).Width);
                if (width >= maxWidth)
                    break;
            }

            // Sampled rows may not show the formatted values (e.g. links still being resolved, or empty rows).
            if (col >= this._firstDataColumn) {
                switch (formatter.GetField(col - this._firstDataColumn)) {
                    case { Kind: ExcelSchemaFieldKind.Link } when formatter.LinkResolver is not null:
                        width = Math.Max(width, linkMinWidth);
                        break;
                    case { Kind: ExcelSchemaFieldKind.ModelId }:
                        width = Math.Max(width, modelIdMinWidth);
                        break;
                }
            }

            column.Width = Math.Clamp(
                width + padding + column.DefaultCellStyle.Padding.Horizontal,
                minWidth,
                maxWidth);
        }
    }

    private (ExcelSheetPage Page, ExcelSheetRow Row) RowAt(int viewIndex) =>
        this._filter is { } filter ? this._rows[filter[viewIndex]] : this._rows[viewIndex];

    private int FindViewIndex(uint rowId, ushort subrowId)
    {
        // Binary search for the first row with (RowId, SubrowId) >= target; rows are sorted.
        var target = ((ulong) rowId << 16) | subrowId;
        var lo = 0;
        var hi = this.ShownRowCount;
        while (lo < hi) {
            var mid = lo + (hi - lo) / 2;
            var row = this.RowAt(mid).Row;
            var key = ((ulong) row.RowId << 16) | row.SubrowId;
            if (key < target)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private void ScrollToViewIndex(int index)
    {
        if (index < 0 || index >= this._grid.RowCount)
            return;

        var column = this._pendingGotoColumn is { } sheetColumn
            ? this._firstDataColumn + sheetColumn
            : this._grid.CurrentCell?.ColumnIndex ?? this._firstDataColumn;
        this._pendingGotoColumn = null;
        if (column < 0 || column >= this._grid.ColumnCount)
            column = 0;

        try {
            this._grid.FirstDisplayedScrollingRowIndex = index;
            this._grid.ClearSelection();
            this._grid.CurrentCell = this._grid[column, index];
        } catch (InvalidOperationException) {
            // Can happen while the grid is not yet laid out; ignore.
        }
    }

    private string GetCellText(int viewIndex, int columnIndex, ExcelSheetFormatter formatter)
    {
        var (page, row) = this.RowAt(viewIndex);
        if (columnIndex == 0)
            return row.RowId.ToString(CultureInfo.InvariantCulture);
        if (columnIndex < this._firstDataColumn)
            return row.SubrowId.ToString(CultureInfo.InvariantCulture);
        return formatter.GetText(page, row, columnIndex - this._firstDataColumn);
    }

    /// <summary>Gets the name of a column for exporting; the field name if the schema is in use.</summary>
    private string GetColumnExportName(int columnIndex) =>
        columnIndex >= this._firstDataColumn &&
        this._formatter?.GetField(columnIndex - this._firstDataColumn) is { } field
            ? field.Name
            : this._grid.Columns[columnIndex].HeaderText.Replace('\n', ' ');

    private void GridOnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (this._formatter is not { } formatter || e.RowIndex < 0 || e.RowIndex >= this.ShownRowCount)
            return;
        e.Value = this.GetCellText(e.RowIndex, e.ColumnIndex, formatter);
    }

    private void GridOnCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (this._formatter is not { } formatter ||
            e.RowIndex < 0 ||
            e.RowIndex >= this.ShownRowCount ||
            e.ColumnIndex < this._firstDataColumn)
            return;

        var (page, row) = this.RowAt(e.RowIndex);
        var tip = formatter.GetToolTip(page, row, e.ColumnIndex - this._firstDataColumn);
        if (this.GetLinkToolTip(e.RowIndex, e.ColumnIndex) is { } linkTip)
            tip = tip is null ? linkTip : $"{tip}\n{linkTip}";
        if (tip is null)
            return;

        var text = this.GetCellText(e.RowIndex, e.ColumnIndex, formatter);
        e.ToolTipText = text.Length == 0 ? tip : $"{text}\n{tip}";
    }

    /// <summary>Draws a swatch for color fields.</summary>
    private void GridOnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (this._formatter is not { } formatter ||
            e.Graphics is not { } g ||
            e.RowIndex < 0 ||
            e.RowIndex >= this.ShownRowCount ||
            e.ColumnIndex < this._firstDataColumn)
            return;

        var (page, row) = this.RowAt(e.RowIndex);
        if (formatter.GetSwatchColor(page, row, e.ColumnIndex - this._firstDataColumn) is not { } argb)
            return;

        e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
        var size = Math.Max(4, e.CellBounds.Height - this.LogicalToDeviceUnits(8));
        var rc = new Rectangle(
            e.CellBounds.Left + this.LogicalToDeviceUnits(4),
            e.CellBounds.Top + (e.CellBounds.Height - size) / 2,
            size * 3 / 2,
            size);
        using (var brush = new SolidBrush(Color.FromArgb(unchecked((int) argb))))
            g.FillRectangle(brush, rc);
        g.DrawRectangle(SystemPens.ControlDarkDark, rc);
        e.Handled = true;
    }

    private void GridOnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (this._source is not { } source ||
            this._grid.CurrentCell is not { } cell ||
            cell.RowIndex < 0 ||
            cell.RowIndex >= this.ShownRowCount) {
            this._selectionStatus.Text = string.Empty;
            return;
        }

        var row = this.RowAt(cell.RowIndex).Row;
        var rowText = source.HasSubrows ? $"Row {row.RowId}.{row.SubrowId}" : $"Row {row.RowId}";
        if (cell.ColumnIndex >= this._firstDataColumn) {
            var column = source.Columns[cell.ColumnIndex - this._firstDataColumn];
            var field = this._formatter?.GetField(column.Index);
            var text = field is null
                ? $"{rowText}, column {column.Index} ({column.Type} {column.LocationText})"
                : $"{rowText}, {field.Name} (column {column.Index}, {column.Type} {column.LocationText}, " +
                $"{field.KindName})";
            if (this._formatter?.GetToolTip(this.RowAt(cell.RowIndex).Page, row, column.Index) is { } tip)
                text += " | " + tip.Replace('\n', ' ');
            this._selectionStatus.Text = text;
        } else {
            this._selectionStatus.Text = rowText;
        }
    }

    private void LanguageComboOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this._suppressLanguageChange || this._languageCombo.SelectedItem is not LanguageItem item)
            return;
        if (item.Language == this._language)
            return;

        // Keep the current position.
        if (this._grid.CurrentCell is { RowIndex: >= 0 } cell && cell.RowIndex < this.ShownRowCount) {
            var row = this.RowAt(cell.RowIndex).Row;
            this._pendingGoto = (row.RowId, row.SubrowId);
        }

        this.StartLoading(item.Language);
    }

    private void FormatButtonOnCheckedChanged(object? sender, EventArgs e)
    {
        this.UpdateFormatter();
        if (sender == this._schemaButton || sender == this._resolveLinksButton)
            this.UpdateColumnHeaders();
        this.GridOnCurrentCellChanged(null, EventArgs.Empty);
        if (this._appliedFilter != string.Empty)
            this.ApplyFilter(true);
    }

    private void FilterBoxOnTextChanged(object? sender, EventArgs e)
    {
        this._filterTimer.Stop();
        this._filterTimer.Start();
    }

    private void FilterBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;
        e.Handled = e.SuppressKeyPress = true;
        this._filterTimer.Stop();
        this.ApplyFilter();
    }

    private void GotoBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter)
            return;
        e.Handled = e.SuppressKeyPress = true;

        if (!TryParseRowKey(this._gotoBox.Text, out var rowId, out var subrowId)) {
            this._progressStatus.Text = $"Invalid row ID: \"{this._gotoBox.Text}\"";
            return;
        }

        if (this.GoToRow(rowId, subrowId)) {
            this._progressStatus.Text = string.Empty;
            this._grid.Focus();
        } else if (this._pendingGoto is null) {
            this._progressStatus.Text = $"Row {this._gotoBox.Text.Trim()} not found; showing the nearest row.";
            this._grid.Focus();
        }
    }

    private static bool TryParseRowKey(string text, out uint rowId, out ushort subrowId)
    {
        rowId = 0;
        subrowId = 0;
        text = text.Trim().TrimStart('#');
        if (text.Length == 0)
            return false;

        var parts = text.Split('.', ':', ',');
        if (parts.Length > 2)
            return false;

        if (!TryParseUInt(parts[0], out rowId))
            return false;
        if (parts.Length == 2) {
            if (!TryParseUInt(parts[1], out var s) || s > ushort.MaxValue)
                return false;
            subrowId = (ushort) s;
        }

        return true;

        static bool TryParseUInt(string s, out uint value) =>
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                : uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private async void ApplyFilter(bool force = false)
    {
        var query = this._filterBox.Text.Trim();
        if (!force && query == this._appliedFilter)
            return;

        this._filterCancel.Cancel();
        var cts = this._filterCancel = CancellationTokenSource.CreateLinkedTokenSource(this._loadCancel.Token);

        if (query == string.Empty) {
            this.SetFilter(null, string.Empty);
            return;
        }

        try {
            this._progressStatus.Text = "Filtering...";
            while (!this._loadComplete) {
                await this._loadTask;
                if (cts.IsCancellationRequested || this.IsDisposed)
                    return;
            }

            if (this._formatter is not { } formatter)
                return;

            if (formatter.LinkResolver is { } resolver) {
                this._progressStatus.Text = "Loading linked sheets...";
                await resolver.PreloadAsync(formatter.GetAllLinkTargets(), formatter.Language);
                if (cts.IsCancellationRequested || this.IsDisposed)
                    return;
                this._progressStatus.Text = "Filtering...";
            }

            var rows = this._rows.ToArray();
            var result = await Task.Run(() => Filter(rows, formatter, query, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || this.IsDisposed)
                return;

            this.SetFilter(result, query);
            this._progressStatus.Text = string.Empty;
        } catch (OperationCanceledException) {
            // ignore
        } catch (Exception e) {
            if (!this.IsDisposed)
                this._progressStatus.Text = $"Filter failed: {e.Message}";
        }
    }

    private static int[] Filter(
        (ExcelSheetPage Page, ExcelSheetRow Row)[] rows,
        ExcelSheetFormatter formatter,
        string query,
        CancellationToken cancellationToken)
    {
        var source = formatter.Source;
        // Skip columns whose formatted values can never contain the query.
        var mayBeNumber = query.All(c => char.IsAsciiHexDigit(c) || c is '-' or '+' or '.' or 'x' or 'X' or 'E');
        var mayBeBool = "true".Contains(query, StringComparison.OrdinalIgnoreCase) ||
            "false".Contains(query, StringComparison.OrdinalIgnoreCase);
        var columns = source.Columns
            .Where(
                x => x.IsString ||
                    formatter.MayHaveFormattedText(x.Index) ||
                    (x.IsNumeric && mayBeNumber) ||
                    (!x.IsString && !x.IsNumeric && (mayBeBool || mayBeNumber)))
            .ToArray();
        var hasSubrows = source.HasSubrows;

        return Enumerable.Range(0, rows.Length)
            .AsParallel()
            .AsOrdered()
            .WithCancellation(cancellationToken)
            .Where(
                i => {
                    var (page, row) = rows[i];
                    if (mayBeNumber) {
                        var key = hasSubrows ? $"{row.RowId}.{row.SubrowId}" : $"{row.RowId}";
                        if (key.Contains(query, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    foreach (var column in columns) {
                        if (formatter.GetText(page, row, column.Index)
                            .Contains(query, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    return false;
                })
            .ToArray();
    }

    private void SetFilter(int[]? filter, string query)
    {
        (uint, ushort)? currentKey = null;
        if (this._grid.CurrentCell is { RowIndex: >= 0 } cell && cell.RowIndex < this.ShownRowCount) {
            var row = this.RowAt(cell.RowIndex).Row;
            currentKey = (row.RowId, row.SubrowId);
        }

        this._grid.RowCount = 0;
        this._filter = filter;
        this._appliedFilter = query;
        this._grid.RowCount = this.ShownRowCount;
        this.UpdateRowsStatus();

        if (currentKey is { } k && this.ShownRowCount > 0) {
            var index = Math.Min(this.FindViewIndex(k.Item1, k.Item2), this.ShownRowCount - 1);
            this.ScrollToViewIndex(index);
        }
    }

    private ContextMenuStrip CreateGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        this.AddLinkMenuItems(menu);
        menu.Items.Add(
            new ToolStripMenuItem(
                "&Copy",
                null,
                (_, _) => this.CopySelection(false)) {
                ShortcutKeyDisplayString = "Ctrl+C",
            });
        menu.Items.Add(
            new ToolStripMenuItem(
                "Copy with &headers",
                null,
                (_, _) => this.CopySelection(true)) {
                ShortcutKeyDisplayString = "Ctrl+Shift+C",
            });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("&Go to row...", null, (_, _) => this._gotoBox.Focus()) {
            ShortcutKeyDisplayString = "Ctrl+G",
        });
        menu.Items.Add(new ToolStripMenuItem("&Filter rows...", null, (_, _) => this._filterBox.Focus()) {
            ShortcutKeyDisplayString = "Ctrl+F",
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("&Export CSV...", null, (_, _) => this.ExportCsv()));
        return menu;
    }

    private void GridOnCopyRequested(object? sender, bool withHeaders) => this.CopySelection(withHeaders);

    /// <summary>Copies the selected cells as tab separated values.</summary>
    /// <remarks>Rows and columns that only partially contain selected cells are filled with empty values.</remarks>
    public void CopySelection(bool withHeaders)
    {
        if (this._formatter is not { } formatter)
            return;

        var cells = this._grid.SelectedCells;
        if (cells.Count == 0)
            return;

        var selected = new HashSet<(int Row, int Column)>(cells.Count);
        var rowSet = new SortedSet<int>();
        var colSet = new SortedSet<int>();
        foreach (DataGridViewCell cell in cells) {
            if (cell.RowIndex < 0 || cell.RowIndex >= this.ShownRowCount)
                continue;
            selected.Add((cell.RowIndex, cell.ColumnIndex));
            rowSet.Add(cell.RowIndex);
            colSet.Add(cell.ColumnIndex);
        }

        // Order columns by display order.
        var cols = colSet.OrderBy(x => this._grid.Columns[x].DisplayIndex).ToArray();
        var sb = new StringBuilder();
        if (withHeaders) {
            sb.AppendJoin('\t', cols.Select(x => EscapeTsv(this.GetColumnExportName(x))));
            sb.Append("\r\n");
        }

        foreach (var row in rowSet) {
            var first = true;
            foreach (var col in cols) {
                if (!first)
                    sb.Append('\t');
                first = false;
                if (selected.Contains((row, col)))
                    sb.Append(EscapeTsv(this.GetCellText(row, col, formatter)));
            }

            sb.Append("\r\n");
        }

        try {
            Clipboard.SetText(sb.ToString());
            this._progressStatus.Text = $"Copied {selected.Count:N0} cell(s).";
        } catch (Exception e) {
            this._progressStatus.Text = $"Copy failed: {e.Message}";
        }

        static string EscapeTsv(string s) =>
            s.Contains('\t') || s.Contains('\n') || s.Contains('\r') || s.Contains('"')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;
    }

    private async void ExportCsv()
    {
        if (this._source is not { } source)
            return;

        var langCode = ExcelSheetFileNames.GetLanguageCode(this._language);
        using var sfd = new SaveFileDialog();
        sfd.OverwritePrompt = true;
        sfd.ClientGuid = ExcelViewerSaveToGuid;
        sfd.Title = $"Export {source.SheetName}";
        sfd.Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*";
        sfd.FileName = source.SheetName.Replace('/', '_') + (langCode == string.Empty ? "" : "_" + langCode) +
            ".csv";
        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(this._loadCancel.Token);
        var path = sfd.FileName;
        try {
            this._exportButton.Enabled = false;
            this._progressStatus.Text = "Exporting...";
            while (!this._loadComplete) {
                await this._loadTask;
                if (cts.IsCancellationRequested || this.IsDisposed)
                    return;
            }

            if (this._formatter is not { } formatter)
                return;

            if (formatter.LinkResolver is { } resolver) {
                this._progressStatus.Text = "Loading linked sheets...";
                await resolver.PreloadAsync(formatter.GetAllLinkTargets(), formatter.Language);
                if (cts.IsCancellationRequested || this.IsDisposed)
                    return;
                this._progressStatus.Text = "Exporting...";
            }

            var rows = this._filter is { } filter
                ? filter.Select(x => this._rows[x]).ToArray()
                : this._rows.ToArray();
            var headers = ExcelCsvExporter.GetHeaders(formatter);
            await Task.Run(
                () => {
                    using var stream = File.Create(path);
                    ExcelCsvExporter.Write(stream, formatter, rows, headers, cts.Token);
                },
                cts.Token);
            if (!this.IsDisposed)
                this._progressStatus.Text = $"Exported {rows.Length:N0} rows to {path}";
        } catch (OperationCanceledException) {
            // ignore
        } catch (Exception e) {
            if (!this.IsDisposed) {
                this._progressStatus.Text = "Export failed.";
                MessageBox.Show(this, $"Failed to export.\n\n{e.Message}", this.Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        } finally {
            if (!this.IsDisposed)
                this._exportButton.Enabled = true;
        }
    }

    private sealed class LanguageItem(Language language) {
        public Language Language { get; } = language;

        public override string ToString() => ExcelSheetFileNames.GetLanguageDisplayName(this.Language);
    }

    /// <summary>A double buffered grid, which does not handle Ctrl+A (selecting millions of cells individually
    /// is very slow) and reports Ctrl+C instead of using the built-in clipboard support.</summary>
    private sealed class BufferedDataGridView : DataGridView {
        public BufferedDataGridView() => this.DoubleBuffered = true;

        public event EventHandler<bool>? CopyRequested;

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            switch (e.KeyData) {
                case Keys.Control | Keys.A:
                    return true;
                case Keys.Control | Keys.C:
                case Keys.Control | Keys.Insert:
                    this.CopyRequested?.Invoke(this, false);
                    return true;
                case Keys.Control | Keys.Shift | Keys.C:
                    this.CopyRequested?.Invoke(this, true);
                    return true;
            }

            return base.ProcessDataGridViewKey(e);
        }
    }
}
