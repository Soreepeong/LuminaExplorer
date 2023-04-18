using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private const int ImageListIndexFile = 0;
    private const int ImageListIndexFolder = 1;

    private readonly VirtualSqPackTree _vsp;
    
    public Explorer(VirtualSqPackTree vsp) {
        _vsp = vsp;

        InitializeComponent();
        
        Constructor_FileTree();
        Constructor_FileList();
        Constructor_Navigation();

        NavigateTo(_vsp.RootFolder, true);

        txtSearch.TextBox!.PlaceholderText = @"Search...";
        
        // ExpandTreeTo("/chara/monster/");

        // random folder with a lot of images
        // ExpandTreeTo("/common/graphics/texture");

        // mustadio
        // ExpandTreeTo("/chara/monster/m0361/obj/body/b0003/texture/");

        // construct 14
        // ExpandTreeTo("/chara/monster/m0489/animation/a0001/bt_common/loop_sp/");
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Dispose_Navigation();
            Dispose_FileList();
            Dispose_FileTree();

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
                NavigateBack();
                return true;
            case Keys.BrowserForward:
                NavigateForward();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void Explorer_Shown(object sender, EventArgs e) {
        lvwFiles.Focus();
    }
}
