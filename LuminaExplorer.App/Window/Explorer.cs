using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private PreviewHandler? _previewHandler;
    private FileListHandler? _fileListHandler;
    private NavigationHandler? _navigationHandler;
    private FileTreeHandler? _fileTreeHandler;
    private SearchHandler? _searchHandler;
    private VirtualSqPackTree? _tree;
    private AppConfig _appConfig;

    public Explorer(AppConfig? appConfig = default, VirtualSqPackTree? tree = default) {
        InitializeComponent();

        _appConfig = appConfig ?? new();
        _tree = tree;
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

    public VirtualSqPackTree? Tree {
        get => _tree;
        set {
            if (_tree == value)
                return;

            _tree = value;
            if (_fileListHandler is not null)
                _fileListHandler.Tree = value;
            if (_navigationHandler is not null)
                _navigationHandler.Tree = value;
            if (_fileTreeHandler is not null)
                _fileTreeHandler.Tree = value;
            if (_searchHandler is not null)
                _searchHandler.Tree = value;
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Hide();
            SafeDispose.D(ref _previewHandler);
            SafeDispose.D(ref _fileListHandler);
            SafeDispose.D(ref _navigationHandler);
            SafeDispose.D(ref _fileTreeHandler);
            SafeDispose.D(ref _searchHandler);

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
