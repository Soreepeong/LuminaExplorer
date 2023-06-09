using System;
using System.Windows.Forms;
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

    public Explorer(AppConfig? appConfig = default, IVirtualFileSystem? vfs = default) {
        InitializeComponent();

        _appConfig = appConfig ?? new();
        _vfs = vfs;
        _previewHandler = new(this);
        _fileListHandler = new(this);
        _navigationHandler = new(this);
        _fileTreeHandler = new(this);
        _searchHandler = new(this);

        _fileTreeHandler.ExpandTreeTo(AppConfig.LastFolder);
        _ = _navigationHandler.NavigateTo(AppConfig.LastFolder);
    }

    public AppConfig AppConfig {
        get => _appConfig;
        set {
            if (_appConfig == value)
                return;

            _appConfig = value with { };
            if (_fileListHandler is not null)
                _fileListHandler.AppConfig = _appConfig;
            if (_navigationHandler is not null)
                _navigationHandler.AppConfig = _appConfig;
            if (_searchHandler is not null)
                _searchHandler.AppConfig = _appConfig;
        }
    }

    public IVirtualFileSystem? Vfs {
        get => _vfs;
        set {
            if (_vfs == value)
                return;

            _vfs = value;
            if (_fileListHandler is not null)
                _fileListHandler.Vfs = value;
            if (_navigationHandler is not null)
                _navigationHandler.Vfs = value;
            if (_fileTreeHandler is not null)
                _fileTreeHandler.Vfs = value;
            if (_searchHandler is not null)
                _searchHandler.Vfs = value;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Hide();
            SafeDispose.One(ref _previewHandler);
            SafeDispose.One(ref _fileListHandler);
            SafeDispose.One(ref _navigationHandler);
            SafeDispose.One(ref _fileTreeHandler);
            SafeDispose.One(ref _searchHandler);

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        switch (keyData) {
            case Keys.Control | Keys.F:
            case Keys.BrowserSearch:
                txtSearch.Focus();
                return true;
            case Keys.F4:
                txtPath.Focus();
                return true;
            case Keys.BrowserBack:
                _navigationHandler?.NavigateBack();
                return true;
            case Keys.BrowserForward:
                _navigationHandler?.NavigateForward();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void Explorer_Shown(object sender, EventArgs e) {
        lvwFiles.Focus();
    }
}
