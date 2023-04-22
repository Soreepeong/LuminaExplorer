using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class TextureViewer : Form {
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;

    private static readonly string[] ValidTextureExtensions = {".tex", ".atex"};

    private readonly MouseActivityTracker _panelMouseTracker;
    private int _unconstrainedPanelWidth = 240;

    private readonly CancellationTokenSource _closeToken = new();

    private readonly List<VirtualFile> _playlist = new();
    private VirtualFolder? _folder;
    private int _indexInPlaylist;
    private VirtualSqPackTree? _tree;

    private bool _isFullScreen;
    private FormBorderStyle _nonFullScreenBorderStyle;
    private FormWindowState _nonFullScreenWindowState;
    private Size _nonFullScreenSize;
    private bool _nonFullScreenControlBox;

    private Task _navigationTask = Task.CompletedTask;

    public TextureViewer() {
        InitializeComponent();

        TexViewer.MouseActivity.LeftClick += TexViewerOnLeftClick;

        _panelMouseTracker = new(PropertyPanel);
        _panelMouseTracker.UseLeftDrag = true;
        _panelMouseTracker.Pan += PanelMouseTrackerOnPan;

        PropertyPanel.Visible = false;
        PropertyPanel.VisibleChanged += PropertyPanelOnVisibleChanged;

        TexViewer.Margin = new(); // required line
        TexViewer.KeyDown += TexViewerOnKeyDown;
        TexViewer.MouseDown += TexViewerOnMouseDown;
    }

    public TimeSpan OverlayShortDuration = TimeSpan.FromSeconds(0.5);

    public TimeSpan OverlayLongDuration = TimeSpan.FromSeconds(1);

    public bool IsFullScreen {
        get => _isFullScreen;
        set {
            if (value == _isFullScreen)
                return;

            _isFullScreen = value;
            if (!value) {
                FormBorderStyle = _nonFullScreenBorderStyle;
                ControlBox = _nonFullScreenControlBox;
                WindowState = _nonFullScreenWindowState;
                Size = _nonFullScreenSize;
            } else {
                _nonFullScreenSize = Size;

                _nonFullScreenWindowState = WindowState;
                WindowState = FormWindowState.Maximized;

                _nonFullScreenControlBox = ControlBox;
                ControlBox = false;

                _nonFullScreenBorderStyle = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;
            }
        }
    }

    private void TexViewerOnMouseDown(object? sender, MouseEventArgs e) {
        TexViewer.Focus();
    }

    private void TexViewerOnKeyDown(object? sender, KeyEventArgs e) {
        switch (e.KeyCode) {
            case Keys.D1: // TODO: set default zoom to fit in window
                break;
            case Keys.Z: // TODO: above + stretch to fit
                break;
            case Keys.D0: // TODO: zoom to 100%
                break;
            case Keys.T: // TODO: Toggle alpha channel; independent from below
                break;
            case Keys.R: // TODO: toggle R-only mode
                break;
            case Keys.G: // TODO: toggle G-only mode
                break;
            case Keys.B: // TODO: toggle B-only mode
                break;
            case Keys.A: // TODO: toggle A-only mode
                break;
            case Keys.Oemplus: // TODO: zoom in
                break;
            case Keys.OemMinus: // TODO: zoom out 
                break;
            case Keys.Up when e.Alt: // TODO: 0deg rotate
                break;
            case Keys.Right when e.Alt: // TODO: 90deg cw rotate
                break;
            case Keys.Down when e.Alt: // TODO: 180deg rotate
                break;
            case Keys.Left when e.Alt: // TODO: 90deg ccw rotate
                break;
            case Keys.Enter:
                IsFullScreen = !IsFullScreen;
                if (IsFullScreen)
                    TexViewer.ShowOverlayString("Press Enter key again to exit full screen mode.",
                        OverlayShortDuration);
                break;
            case Keys.Escape:
                if (IsFullScreen) {
                    IsFullScreen = false;
                    TexViewer.ShowOverlayString("Press Esc key again to close.", OverlayShortDuration);
                } else
                    Close();

                break;
            case Keys.PageUp:
                if (_navigationTask.IsCompleted)
                    _navigationTask = Task.Run(
                        () => FindAndSelectFirstTexFile(_indexInPlaylist - 1, -1, _closeToken.Token),
                        _closeToken.Token);
                break;
            case Keys.PageDown:
                if (_navigationTask.IsCompleted)
                    _navigationTask = Task.Run(
                        () => FindAndSelectFirstTexFile(_indexInPlaylist + 1, 1, _closeToken.Token),
                        _closeToken.Token);
                break;
            case Keys.Home:
                if (_navigationTask.IsCompleted)
                    _navigationTask = Task.Run(
                        () => FindAndSelectFirstTexFile(0, 1, _closeToken.Token),
                        _closeToken.Token);
                break;
            case Keys.End:
                if (_navigationTask.IsCompleted)
                    _navigationTask = Task.Run(
                        () => FindAndSelectFirstTexFile(_playlist.Count - 1, -1, _closeToken.Token),
                        _closeToken.Token);
                break;
        }
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
                TexViewer.Viewport.Pan.X + prev.X + prev.Width / 2f - curr.X - curr.Width / 2f,
                TexViewer.Viewport.Pan.Y + prev.Y + prev.Height / 2f - curr.Y - curr.Height / 2f);
        }
    }

    public void SetFile(VirtualSqPackTree tree, VirtualFile file, TexFile texFile, VirtualFolder? folder,
        IEnumerable<VirtualFile> playlist) {
        _tree = tree;
        _folder = folder;
        _playlist.Clear();
        _playlist.AddRange(playlist);
        _indexInPlaylist = _playlist.IndexOf(file);
        if (_indexInPlaylist < 0)
            _playlist.Insert(_indexInPlaylist = 0, file);
        SelectFile(_indexInPlaylist, texFile);
    }

    private async Task<bool> FindAndSelectFirstTexFile(int index, int step, CancellationToken cancellationToken,
        bool cycle = true) {
        if (_tree is not { } tree)
            return false;

        var invalidIndices = new List<int>();
        TexFile? texFile = null;
        for (; index >= 0 && index < _playlist.Count; index += step) {
            var item = _playlist[index];

            if (item.NameResolved && ValidTextureExtensions.All(
                    x => !item.Name.EndsWith(x, StringComparison.InvariantCultureIgnoreCase))) {
                invalidIndices.Add(index);
                continue;
            }

            var lookup = tree.GetLookup(item);
            try {
                texFile = await lookup.AsFileResource<TexFile>(cancellationToken);
                break;
            } catch (Exception) {
                invalidIndices.Add(index);
            }
        }

        if (invalidIndices.Any()) {
            if (step > 0) {
                foreach (var i in Enumerable.Reverse(invalidIndices))
                    _playlist.RemoveAt(i);
            } else {
                foreach (var i in invalidIndices)
                    _playlist.RemoveAt(i);
            }
        }

        if (texFile is not null) {
            await TexViewer.RunOnUiThread(() => {
                SelectFile(index, texFile);
                if (cycle)
                    TexViewer.ShowOverlayString(null, OverlayShortDuration);
            });
            return true;
        }

        if (_playlist.Count == 0) {
            await TexViewer.RunOnUiThread(() =>
                TexViewer.ShowOverlayString("No vaild texture file could be found.", OverlayLongDuration));
            return false;
        }

        if (!cycle)
            return false;

        if (index < 0) {
            if (await FindAndSelectFirstTexFile(_playlist.Count - 1, -1, cancellationToken, false)) {
                await TexViewer.RunOnUiThread(() =>
                    TexViewer.ShowOverlayString(
                        _folder is null
                            ? "This is the last file in this search."
                            : "This is the last file in this folder.",
                        OverlayLongDuration));
                return true;
            }

            return false;
        } else {
            if (await FindAndSelectFirstTexFile(0, 1, cancellationToken, false)) {
                await TexViewer.RunOnUiThread(() =>
                    TexViewer.ShowOverlayString(
                        _folder is null
                            ? "This is the first file in this search."
                            : "This is the first file in this folder.",
                        OverlayLongDuration));
                return true;
            }

            return false;
        }
    }

    private void SelectFile(int index, TexFile texFile) {
        if (_tree is not { } tree)
            return;
        _indexInPlaylist = index;
        var file = _playlist[index];
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

    protected override void OnFormClosed(FormClosedEventArgs e) {
        _closeToken.Cancel();
        base.OnFormClosed(e);
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
