using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class TextureViewer : Form {
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;
    
    private readonly MouseActivityTracker _panelMouseTracker;
    private int _unconstrainedPanelWidth = 240;

    public TextureViewer() {
        InitializeComponent();
        
        TexViewer.MouseActivity.LeftClick += TexViewerOnLeftClick;

        _panelMouseTracker = new(PropertyPanel);
        _panelMouseTracker.UseLeftDrag = true;
        _panelMouseTracker.Pan += PanelMouseTrackerOnPan;

        PropertyPanel.VisibleChanged += PropertyPanelOnVisibleChanged;
        
        PropertyPanel.Visible = false;
        TexViewer.Margin = new();
    }

    private void TexViewerOnLeftClick(Point cursor) {
        using (this.DisableRedrawScoped()) {
            var prev = RectangleToScreen(new(
                TexViewer.Left + TexViewer.Margin.Left,
                TexViewer.Top + TexViewer.Margin.Top,
                TexViewer.Width - TexViewer.Margin.Horizontal,
                TexViewer.Height - TexViewer.Margin.Vertical));

            PropertyPanel.Visible ^= true;
            if (WindowState == FormWindowState.Normal) {
                if (PropertyPanel.Visible) {
                    var screen = Screen.FromControl(this);
                    var newWidth = Math.Min(Width + PropertyPanel.Width, screen.WorkingArea.Width);
                    var newLeft = Left + newWidth > screen.WorkingArea.Right
                        ? screen.WorkingArea.Right - newWidth
                        : Left;
                    SetBounds(newLeft, Top, newWidth, Height);
                } else {
                    Width -= PropertyPanel.Width;
                }
            }

            var curr = RectangleToScreen(new(
                TexViewer.Left + TexViewer.Margin.Left,
                TexViewer.Top + TexViewer.Margin.Top,
                TexViewer.Width - TexViewer.Margin.Horizontal,
                TexViewer.Height - TexViewer.Margin.Vertical));
            TexViewer.Viewport.Pan = new(
                TexViewer.Viewport.Pan.X + prev.X + prev.Width / 2 - curr.X - curr.Width / 2,
                TexViewer.Viewport.Pan.Y + prev.Y + prev.Height / 2 - curr.Y - curr.Height / 2);
        }
    }

    public void SetFile(VirtualSqPackTree tree, VirtualFile file, TexFile texFile) {
        Text = tree.GetFullPath(file);
        TexViewer.SetFile(tree, file, texFile);
        PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(texFile);
    }

    public void ShowRelativeTo(Control opener) {
        var rc = TexViewer.GetViewportRectangleSuggestion(opener);
        if (rc.Width < MinimumWidth) {
            rc.X -= (MinimumWidth - rc.Width) / 2;
            rc.Width = MinimumWidth;
        }
        if (rc.Height < MinimumHeight) {
            rc.X -= (MinimumHeight - rc.Height) / 2;
            rc.Height = MinimumHeight;
        }
        SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        Show();
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        ResizePanel(_unconstrainedPanelWidth);
    }

    private void PropertyPanelOnVisibleChanged(object? sender, EventArgs e) {
        if (PropertyPanel.Visible)
            TexViewer.Margin = TexViewer.Margin with {Right = PropertyPanel.Width};
        else
            TexViewer.Margin = new();
    }

    private void PanelMouseTrackerOnPan(Point delta) {
        ResizePanel(PropertyPanel.Width - delta.X);
        _unconstrainedPanelWidth = PropertyPanel.Width;
    }

    private void ResizePanel(int newSuggestedWidth) {
        var clientSize = ClientSize;
        var newPanelWidth = newSuggestedWidth;
        if (newPanelWidth > clientSize.Width)
            newPanelWidth = clientSize.Width;
        if (newPanelWidth < PropertyPanel.Padding.Horizontal)
            newPanelWidth = PropertyPanel.Padding.Horizontal;
        PropertyPanel.SetBounds(clientSize.Width - newPanelWidth, 0, newPanelWidth, clientSize.Height);
        if (PropertyPanel.Visible)
            TexViewer.Margin = TexViewer.Margin with {Right = newPanelWidth};
    }
}
