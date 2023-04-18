using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private PreviewHandler? _previewHandler;
    private FileListHandler? _fileListHandler;
    private NavigationHandler? _navigationHandler;
    private FileTreeHandler? _fileTreeHandler;
    private SearchHandler? _searchHandler;
    private VirtualSqPackTree? _tree;

    public Explorer(VirtualSqPackTree? vsp) {
        InitializeComponent();
        
        _previewHandler = new(this);
        _fileListHandler = new(this);
        _navigationHandler = new(this);
        _fileTreeHandler = new(this);
        _searchHandler = new(this);

        Tree = vsp;

        txtSearch.TextBox!.PlaceholderText = @"Search...";
        
        // ExpandTreeTo("/chara/monster/");

        // random folder with a lot of images
        // ExpandTreeTo("/common/graphics/texture");

        // mustadio
        // ExpandTreeTo("/chara/monster/m0361/obj/body/b0003/texture/");

        // construct 14
        // ExpandTreeTo("/chara/monster/m0489/animation/a0001/bt_common/loop_sp/");
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
            _previewHandler?.Dispose();
            _previewHandler = null;
            _fileListHandler?.Dispose();
            _fileListHandler = null;
            _navigationHandler?.Dispose();
            _navigationHandler = null;
            _fileTreeHandler?.Dispose();
            _fileTreeHandler = null;
            _searchHandler?.Dispose();
            _searchHandler = null;

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
