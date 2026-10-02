using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.ExcelSheets.Index;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    /// <summary>Language to index the sheets in; sheets without it are indexed in the language they have.</summary>
    private const Language ExcelValueIndexLanguage = Language.English;

    private readonly CancellationTokenSource _excelValueIndexCancel = new();
    private ExcelValueIndex? _excelValueIndex;
    private Task? _excelValueIndexTask;
    private bool _excelValueIndexBuildStarted;
    private bool _excelValueIndexNotBuilt;
    private string? _excelValueIndexError;
    private volatile object? _excelValueIndexProgress; // ExcelValueIndexProgress, boxed to be set atomically
    private long _excelValueIndexNextProgressUpdate;

    /// <summary>Index of the text in all Excel sheets, if loaded.</summary>
    public ExcelValueIndex? ExcelValueIndex => this._excelValueIndex;

    /// <summary>
    /// Gets or sets a function that gives the name of a column of a sheet (sheet name, column index), or <c>null</c>
    /// if unknown; used to label the cells found in sheets. Results of later searches use the new function.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, int, string?>? ExcelColumnNameResolver { get; set; }

    /// <summary>Gets the state of the index for display, while it is not available.</summary>
    public string ExcelValueIndexStatusText =>
        this._excelValueIndexError is { } error ? $"(failed to index sheets: {error})"
        : this._excelValueIndexProgress is ExcelValueIndexProgress p ? $"(indexing sheets... {p.Fraction * 100:0}%)"
        : this._excelValueIndexNotBuilt ? "(index not built yet)"
        : "(loading the sheet index...)";

    /// <summary>Gets whether the index is neither loaded nor being built or loaded.</summary>
    public bool IsExcelValueIndexNotBuilt => this._excelValueIndex is null && this._excelValueIndexNotBuilt;

    /// <summary>Builds the index of the text in the sheets in the background, unless it is available.</summary>
    public void BuildExcelValueIndex() => this.LoadExcelValueIndex(true);

    private string? GetExcelValueIndexCachePath() =>
        string.IsNullOrEmpty(this._appConfig.BaseDirectory)
            ? null
            : Path.Combine(
                this._appConfig.BaseDirectory,
                $"excelvalues.{ExcelSheetFileNames.GetLanguageCode(ExcelValueIndexLanguage)}.dat");

    /// <summary>Loads the index of the text in the sheets in the background, if not already loaded or loading.
    /// </summary>
    /// <param name="buildIfMissing">Whether to build the index if no up-to-date index is saved. Building reads all
    /// sheets, so it is done only once a search needs it.</param>
    private void LoadExcelValueIndex(bool buildIfMissing)
    {
        if (this._vfs is not SqpackFileSystem sqfs || this._excelValueIndex is not null)
            return;
        if (this._excelValueIndexTask is { IsCompleted: false })
            return;
        if (this._excelValueIndexTask is not null && (!buildIfMissing || this._excelValueIndexBuildStarted))
            return;

        this._excelValueIndexBuildStarted = buildIfMissing;
        this._excelValueIndexError = null;
        this._excelValueIndexNotBuilt = false;
        var cachePath = this.GetExcelValueIndexCachePath();
        var cancellationToken = this._excelValueIndexCancel.Token;
        var syncContext = SynchronizationContext.Current;
        var schemaProvider = this.ExcelSchemaProvider;

        this._excelValueIndexTask = Task.Run(
                async () => {
                    // Numeric cells are indexed by the game paths they refer to, which depends on the schema.
                    var schemaSet = (await schemaProvider.GetSchemaSetAsync().WaitAsync(cancellationToken)).Set;
                    var versionKey = ExcelValueIndex.GetGameVersionKey(sqfs.InstallationSqPackDirectory) +
                        $"schema={schemaSet?.Branch ?? "-"};";
                    if (cachePath is not null &&
                        ExcelValueIndex.TryLoad(cachePath, versionKey, ExcelValueIndexLanguage) is { } cached)
                        return cached;
                    if (!buildIfMissing)
                        return null;

                    var sw = Stopwatch.StartNew();
                    var index = await ExcelValueIndexBuilder.BuildAsync(
                        sqfs,
                        sqfs.RootFolder,
                        ExcelValueIndexLanguage,
                        versionKey,
                        p => this.OnExcelValueIndexProgress(p, syncContext),
                        0,
                        cancellationToken,
                        schemaSet,
                        ExcelDerivedReferences.Get(GamePathResolver.Get(sqfs)));
                    Debug.WriteLine(
                        $"Excel value index: {index.ValueCount} values in {index.CellCount} cells " +
                        $"({index.DerivedCellCount} derived) of {index.SheetNames.Count} sheets, built in " +
                        $"{sw.ElapsedMilliseconds}ms");

                    if (cachePath is not null) {
                        try {
                            index.Save(cachePath);
                        } catch (Exception e) {
                            Debug.WriteLine($"Failed to save the excel value index: {e}");
                        }
                    }

                    return index;
                },
                cancellationToken)
            .ContinueWith(
                r => {
                    if (this.IsDisposed || cancellationToken.IsCancellationRequested)
                        return;

                    if (!r.IsCompletedSuccessfully) {
                        this._excelValueIndexError = r.Exception?.InnerException?.Message ?? "unknown error";
                        this._fileTreeHandler?.UpdateExcelValueIndexStatus();
                        this._previewHandler?.UpdateExcelValueIndexStatus();
                        return;
                    }

                    if (r.Result is not { } index) {
                        // No saved index; it is built once a search needs it.
                        this._excelValueIndexNotBuilt = true;
                        this._previewHandler?.UpdateExcelValueIndexStatus();
                        return;
                    }

                    this._excelValueIndex = index;
                    this._excelValueIndexProgress = null;
                    this._fileTreeHandler?.OnExcelValueIndexReady();
                    this._previewHandler?.OnExcelValueIndexReady();
                },
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void OnExcelValueIndexProgress(ExcelValueIndexProgress progress, SynchronizationContext? syncContext)
    {
        this._excelValueIndexProgress = progress;

        // Called from worker threads for each sheet; update the display a few times a second.
        var now = Environment.TickCount64;
        var next = Interlocked.Read(ref this._excelValueIndexNextProgressUpdate);
        if (now < next ||
            Interlocked.CompareExchange(ref this._excelValueIndexNextProgressUpdate, now + 250, next) != next)
            return;

        syncContext?.Post(
            _ => {
                if (!this.IsDisposed && this._excelValueIndex is null) {
                    this._fileTreeHandler?.UpdateExcelValueIndexStatus();
                    this._previewHandler?.UpdateExcelValueIndexStatus();
                }
            },
            null);
    }
}
