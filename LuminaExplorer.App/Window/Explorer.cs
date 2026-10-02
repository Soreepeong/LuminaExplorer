using System;
using System.ComponentModel;
using System.Windows.Forms;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private PreviewHandler? _previewHandler;
    private FileListHandler? _fileListHandler;
    private NavigationHandler? _navigationHandler;
    private FileTreeHandler? _fileTreeHandler;
    private SearchHandler? _searchHandler;
    private IVirtualFileSystem? _vfs;
    private AppConfig _appConfig;

    public Explorer(AppConfig? appConfig = default, IVirtualFileSystem? vfs = default)
    {
        this.InitializeComponent();

        this._appConfig = appConfig ?? new();
        this._vfs = vfs;
        this._previewHandler = new(this);
        this._fileListHandler = new(this);
        this._navigationHandler = new(this);
        this._fileTreeHandler = new(this);
        this._searchHandler = new(this);

        this._fileTreeHandler.ExpandTreeTo(this.AppConfig.LastFolder);
        _ = this._navigationHandler.NavigateTo(this.AppConfig.LastFolder);

        this.LoadFolderNames();

        // Building the index takes a while, so it is built only when a search needs it; a saved one is cheap to load.
        this.LoadExcelValueIndex(false);
        this.ExcelColumnNameResolver = this.ResolveExcelColumnName;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppConfig AppConfig {
        get => this._appConfig;
        set {
            if (this._appConfig == value)
                return;

            this._appConfig = value with { };
            if (this._fileListHandler is not null) this._fileListHandler.AppConfig = this._appConfig;
            if (this._navigationHandler is not null) this._navigationHandler.AppConfig = this._appConfig;
            if (this._searchHandler is not null) this._searchHandler.AppConfig = this._appConfig;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IVirtualFileSystem? Vfs {
        get => this._vfs;
        set {
            if (this._vfs == value)
                return;

            this._vfs = value;
            if (this._fileListHandler is not null) this._fileListHandler.Vfs = value;
            if (this._navigationHandler is not null) this._navigationHandler.Vfs = value;
            if (this._fileTreeHandler is not null) this._fileTreeHandler.Vfs = value;
            if (this._searchHandler is not null) this._searchHandler.Vfs = value;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this.Hide();
            this._folderNamesCancel.Cancel();
            this._excelValueIndexCancel.Cancel();
            SafeDispose.One(ref this._previewHandler);
            SafeDispose.One(ref this._fileListHandler);
            SafeDispose.One(ref this._navigationHandler);
            SafeDispose.One(ref this._fileTreeHandler);
            SafeDispose.One(ref this._searchHandler);

            // Files copied to the clipboard cannot be pasted once this process is gone.
            VirtualFileClipboard.ReleaseIfOwned();

            this.components?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData) {
            case Keys.Control | Keys.F:
            case Keys.BrowserSearch:
                this.txtSearch.Focus();
                return true;
            case Keys.F4:
                this.txtPath.Focus();
                return true;
            case Keys.BrowserBack:
                this._navigationHandler?.NavigateBack();
                return true;
            case Keys.BrowserForward:
                this._navigationHandler?.NavigateForward();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void Explorer_Shown(object sender, EventArgs e)
    {
        this.lvwFiles.Focus();
    }
}
