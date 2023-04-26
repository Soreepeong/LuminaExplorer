using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.VirtualFileSystem;
using DialogResult = System.Windows.Forms.DialogResult;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class TextureViewer : Form {
    private const int MinimumWidth = 320;
    private const int MinimumHeight = 240;

    private static readonly Guid TextureViewerSaveToGuid = Guid.Parse("5793cbbc-ae79-4d14-8825-9de07d583848");
    private static readonly string[] ValidTextureExtensions = {".tex", ".atex"};

    private readonly MouseActivityTracker _panelMouseTracker;
    private int _unconstrainedPanelWidth = 240;

    private readonly CancellationTokenSource _closeToken = new();

    private readonly List<Tuple<IVirtualFile, bool>> _playlist = new();
    private IVirtualFolder? _folder;
    private int _indexInPlaylist;
    private IVirtualFileSystem? _tree;

    private bool _isFullScreen;
    private FormBorderStyle _nonFullScreenBorderStyle;
    private FormWindowState _nonFullScreenWindowState;
    private Size _nonFullScreenSize;
    private bool _nonFullScreenControlBox;

    private CancellationTokenSource? _texFileLoadCancelTokenSource;
    private Task _navigationTask = Task.CompletedTask;

    public TextureViewer() {
        InitializeComponent();

        _panelMouseTracker = new(PropertyPanel);
        _panelMouseTracker.UseLeftDrag = true;
        _panelMouseTracker.Pan += PanelMouseTrackerOnPan;

        PropertyPanel.Visible = false;
        PropertyPanel.VisibleChanged += PropertyPanelOnVisibleChanged;
        PropertyPanelGrid.PreviewKeyDown += PropertyPanelGridOnPreviewKeyDown;

        TexViewer.Margin = new(); // required line
        TexViewer.MouseActivity.MiddleClick += MouseActivityOnMiddleClick;
        TexViewer.PreviewKeyDown += TexViewerOnPreviewKeyDown;
        TexViewer.MouseDown += TexViewerOnMouseDown;
        TexViewer.NavigateToNextFile += TexViewerOnNavigateToNextFile;
        TexViewer.NavigateToPrevFile += TexViewerOnNavigateToPrevFile;
    }

    private void MouseActivityOnMiddleClick(Point cursor) => IsFullScreen = !IsFullScreen;

    public bool IsFullScreen {
        get => _isFullScreen;
        set {
            if (value == _isFullScreen)
                return;

            using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

            if (!value) {
                ControlBox = _nonFullScreenControlBox;
                FormBorderStyle = _nonFullScreenBorderStyle;
                WindowState = _nonFullScreenWindowState;
                Size = _nonFullScreenSize;
            } else {
                _nonFullScreenSize = Size;

                _nonFullScreenControlBox = ControlBox;
                ControlBox = false;

                _nonFullScreenBorderStyle = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;

                _nonFullScreenWindowState = WindowState;
                // Setting to normal then maximized is required to enter fullscreen, covering Windows task bar.
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
            }

            _isFullScreen = value;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        switch (keyData) {
            case Keys.S | Keys.Control: {
                if (TexViewer.CurrentBitmapSource is { } source && TexViewer.BitmapSource?.FileName is { } fileName) {
                    using var sfd = new SaveFileDialog();
                    sfd.OverwritePrompt = true;
                    sfd.ClientGuid = TextureViewerSaveToGuid;
                    sfd.Title = $"Save {fileName}";
                    sfd.AddExtension = true;
                    sfd.FileName = fileName;
                    sfd.OverwritePrompt = true;
                    sfd.Filter =
                        ".tex file|*.tex" +
                        "|.dds file|*.dds" +
                        "|.png file(s)|*.png" +
                        "|.jpg file(s)|*.jpg" +
                        "|.bmp file(s)|*.bmp";
                    sfd.FilterIndex = 1;
                    if (sfd.ShowDialog() == DialogResult.OK) {
                        try {
                            switch (sfd.FilterIndex) {
                                case 1: {
                                    using var d = File.Open(sfd.FileName, FileMode.Create, FileAccess.Write);
                                    source.WriteTexFile(d);
                                    break;
                                }
                                case 2: {
                                    using var d = File.Open(sfd.FileName, FileMode.Create, FileAccess.Write);
                                    source.WriteDdsFile(d);
                                    break;
                                }
                                case 3:
                                case 4:
                                case 5: {
                                    for (var i = 0; i < source.ImageCount; i++) {
                                        for (var j = 0; j < source.NumberOfMipmaps(i); j++) {
                                            for (var k = 0; k < source.DepthOfMipmap(i, j); k++) {
                                                using var d = File.Open(Path.Join(
                                                        Path.GetDirectoryName(sfd.FileName),
                                                        Path.ChangeExtension(
                                                            $"{Path.GetFileNameWithoutExtension(sfd.FileName)}.{i}.{j}.{k}._",
                                                            Path.GetExtension(sfd.FileName))),
                                                    FileMode.Create,
                                                    FileAccess.Write);
                                                var t = source.GetWicBitmapSourceAsync(i, j, k);
                                                t.Wait();
                                                t.Result.Save(d, sfd.FilterIndex switch {
                                                    3 => WICConstants.GUID_ContainerFormatPng,
                                                    4 => WICConstants.GUID_ContainerFormatJpeg,
                                                    5 => WICConstants.GUID_ContainerFormatBmp,
                                                    _ => throw new InvalidOperationException(),
                                                });
                                            }
                                        }
                                    }

                                    break;
                                }
                            }
                        } catch (Exception e) {
                            MessageBox.Show(
                                $"Failed to save.\n\n{e}",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }
                }

                return true;
            }
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void TexViewerOnMouseDown(object? sender, MouseEventArgs e) {
        TexViewer.Focus();
    }

    private void PropertyPanelGridOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Tab:
                TogglePropertyGrid();
                break;
        }
    }

    private void TexViewerOnNavigateToPrevFile(object? sender, EventArgs eventArgs) {
        if (_navigationTask.IsCompleted)
            _navigationTask = Task.Run(
                () => FindAndSelectFirstTexFile(_indexInPlaylist - 1, -1, _closeToken.Token),
                _closeToken.Token);
    }

    private void TexViewerOnNavigateToNextFile(object? sender, EventArgs eventArgs) {
        if (_navigationTask.IsCompleted)
            _navigationTask = Task.Run(
                () => FindAndSelectFirstTexFile(_indexInPlaylist + 1, 1, _closeToken.Token),
                _closeToken.Token);
    }

    private void TexViewerOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Tab:
                TogglePropertyGrid();
                e.IsInputKey = true;
                break;
            case Keys.Enter:
                IsFullScreen = !IsFullScreen;
                if (IsFullScreen)
                    TexViewer.ShowOverlayStringShort("Press Enter key again to exit full screen mode.");
                break;
            case Keys.Escape:
                if (IsFullScreen) {
                    IsFullScreen = false;
                    TexViewer.ShowOverlayStringShort("Press Esc key again to close.");
                } else if (PropertyPanel.Visible)
                    TogglePropertyGrid();
                else
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

    private void TogglePropertyGrid() {
        using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

        using (this.DisableRedrawScoped()) {
            var prev = RectangleToScreen(new(
                TexViewer.Left + TexViewer.Margin.Left,
                TexViewer.Top + TexViewer.Margin.Top,
                TexViewer.Width - TexViewer.Margin.Horizontal,
                TexViewer.Height - TexViewer.Margin.Vertical));

            PropertyPanel.Visible = !PropertyPanel.Visible;
            if (WindowState == FormWindowState.Normal) {
                if (PropertyPanel.Visible) {
                    var screen = Screen.FromControl(this);
                    var newWidth = Math.Min(Width + PropertyPanel.Width, screen.WorkingArea.Width);
                    var newLeft = Left + newWidth > screen.WorkingArea.Right
                        ? screen.WorkingArea.Right - newWidth
                        : Left;
                    SetBounds(newLeft, Top, newWidth, Height);
                    PropertyPanel.Focus();
                } else {
                    Width -= PropertyPanel.Width;
                    TexViewer.Focus();
                }
            }

            var curr = RectangleToScreen(new(
                TexViewer.Left + TexViewer.Margin.Left,
                TexViewer.Top + TexViewer.Margin.Top,
                TexViewer.Width - TexViewer.Margin.Horizontal,
                TexViewer.Height - TexViewer.Margin.Vertical));
            TexViewer.Pan = new(
                TexViewer.Pan.X + prev.X + prev.Width / 2f - curr.X - curr.Width / 2f,
                TexViewer.Pan.Y + prev.Y + prev.Height / 2f - curr.Y - curr.Height / 2f);
        }
    }

    public void SetFile(IVirtualFileSystem tree, IVirtualFile file, TexFile texFile, IVirtualFolder? folder,
        IEnumerable<IVirtualFile> playlist) {
        _tree = tree;
        _folder = folder;
        _playlist.Clear();
        _playlist.AddRange(playlist.Select(item => Tuple.Create(
            item,
            item.NameResolved && ValidTextureExtensions.All(
                x => !item.Name.EndsWith(x, StringComparison.InvariantCultureIgnoreCase)))));

        var fileTuple = Tuple.Create(file, true);
        _indexInPlaylist = _playlist.IndexOf(fileTuple);
        if (_indexInPlaylist < 0)
            _playlist.Insert(_indexInPlaylist = 0, fileTuple);
        SelectFile(_indexInPlaylist, texFile);
    }

    private async Task<bool> FindAndSelectFirstTexFile(int index, int step, CancellationToken cancellationToken,
        bool cycle = true) {
        if (_tree is not { } tree)
            return false;

        var invalidIndices = new List<int>();
        TexFile? texFile = null;
        for (; index >= 0 && index < _playlist.Count; index += step) {
            var (item, confirmed) = _playlist[index];

            if (confirmed) {
                await TexViewer.RunOnUiThread(() => {
                    SelectFile(index, null);
                    if (cycle)
                        TexViewer.ClearOverlayString();
                });
            }

            if (item.NameResolved && ValidTextureExtensions.All(
                    x => !item.Name.EndsWith(x, StringComparison.InvariantCultureIgnoreCase))) {
                invalidIndices.Add(index);
                continue;
            }

            using var lookup = tree.GetLookup(item);
            try {
                texFile = await lookup.AsFileResource<TexFile>(cancellationToken);
                _playlist[index] = Tuple.Create(item, true);
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
                    TexViewer.ClearOverlayString();
            });
            return true;
        }

        if (_playlist.Count == 0) {
            await TexViewer.RunOnUiThread(() =>
                TexViewer.ShowOverlayStringLong("No vaild texture file could be found."));
            return false;
        }

        if (!cycle)
            return false;

        if (index < 0) {
            if (await FindAndSelectFirstTexFile(_playlist.Count - 1, -1, cancellationToken, false)) {
                await TexViewer.RunOnUiThread(() =>
                    TexViewer.ShowOverlayStringLong(_folder is null
                        ? "This is the last file in this search."
                        : "This is the last file in this folder."));
                return true;
            }

            return false;
        } else {
            if (await FindAndSelectFirstTexFile(0, 1, cancellationToken, false)) {
                await TexViewer.RunOnUiThread(() =>
                    TexViewer.ShowOverlayStringLong(_folder is null
                        ? "This is the first file in this search."
                        : "This is the first file in this folder."));
                return true;
            }

            return false;
        }
    }

    private void SelectFile(int index, TexFile? texFile) {
        if (_tree is not { } tree)
            return;
        _indexInPlaylist = index;
        var (file, _) = _playlist[index];
        Text = tree.GetFullPath(file);

        _texFileLoadCancelTokenSource?.Cancel();
        _texFileLoadCancelTokenSource = null;

        if (texFile is not null) {
            TexViewer.SetFile(texFile);
            PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(texFile);
            return;
        }

        _texFileLoadCancelTokenSource = new();

        using var lookup = tree.GetLookup(file);
        lookup.AsFileResource<TexFile>(_texFileLoadCancelTokenSource.Token)
            .ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully || index != _indexInPlaylist)
                        return;
                    TexViewer.SetFile(r.Result);
                    PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(r.Result);
                },
                _texFileLoadCancelTokenSource.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
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
        if (WindowState == FormWindowState.Normal && IsFullScreen)
            IsFullScreen = false;
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
