using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.GameDataNames;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private readonly CancellationTokenSource _folderNamesCancel = new();
    private CharaFolderNames? _folderNames;
    private ExcelSchemaProvider? _excelSchemaProvider;

    /// <summary>Names of the models in folders under chara/, if loaded.</summary>
    public CharaFolderNames? FolderNames => this._folderNames;

    private void LoadFolderNames()
    {
        if (this._vfs is not SqpackFileSystem sqfs)
            return;

        var appConfig = this._appConfig;
        var cancellationToken = this._folderNamesCancel.Token;
        Task.Run(
                async () => {
                    var cachePath = Path.Combine(appConfig.BaseDirectory, appConfig.BNpcLinkCacheFilePath);
                    if (!File.Exists(cachePath))
                        await CharaFolderNames.TryDownloadAsync(appConfig.BNpcLinkUrl, cachePath, cancellationToken);

                    // Without the link data, names from the game data alone are still available.
                    using var reader = File.Exists(cachePath) ? new StreamReader(cachePath) : null;
                    return CharaFolderNames.Create(sqfs.GameData, reader);
                },
                cancellationToken)
            .ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully || this.IsDisposed)
                        return;

                    this._folderNames = r.Result;
                    this._fileTreeHandler?.RefreshFolderDisplayNames();
                    this._fileListHandler?.RefreshFolderDisplayNames();
                },
                cancellationToken,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Gets the provider of EXDSchema sheet definitions; created on first use.</summary>
    public ExcelSchemaProvider ExcelSchemaProvider {
        get {
            if (this._excelSchemaProvider is not null)
                return this._excelSchemaProvider;

            var appConfig = this._appConfig;
            var gameDirectory = this._vfs is SqpackFileSystem sqfs
                ? sqfs.InstallationSqPackDirectory.Parent?.FullName
                : null;
            // Also used from background tasks; keep a single instance.
            var created = new ExcelSchemaProvider(
                Path.Combine(appConfig.BaseDirectory, appConfig.ExcelSchemaCacheDirectory),
                gameDirectory,
                appConfig.ExcelSchemaRepository);
            return Interlocked.CompareExchange(ref this._excelSchemaProvider, created, null) ?? created;
        }
    }

    public bool TryGetFolderNames(string fullPath, out IReadOnlyList<string> names)
    {
        if (this._folderNames is { } folderNames && folderNames.TryGetNames(fullPath, out names) && names.Count > 0)
            return true;

        names = [];
        return false;
    }

    /// <summary>Gets the folder name, followed by the names of the models in it if known.</summary>
    public string GetFolderDisplayName(IVirtualFileSystem tree, IVirtualFolder folder) =>
        this.GetFolderDisplayName(folder.Name.Trim('/'), tree.GetFullPath(folder));

    public string GetFolderDisplayName(string name, string fullPath) =>
        this.TryGetFolderNames(fullPath, out var names)
            ? $"{name} ({CharaFolderNames.FormatShort(names)})"
            : name;
}
