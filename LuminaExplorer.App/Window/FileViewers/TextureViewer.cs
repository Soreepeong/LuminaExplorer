using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using TerraFX.Interop.Windows;
using DialogResult = System.Windows.Forms.DialogResult;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class TextureViewer : Form {
    private const int MinimumDefaultWidth = 320;
    private const int MinimumDefaultHeight = 240;

    private static readonly Guid TextureViewerSaveToGuid = Guid.Parse("5793cbbc-ae79-4d14-8825-9de07d583848");

    private readonly MouseActivityTracker _panelMouseTracker;
    private int _unconstrainedPanelWidth;

    private readonly CancellationTokenSource _closeToken = new();

    private readonly List<Tuple<IVirtualFile, bool>> _playlist = new();
    private IVirtualFolder? _folder;
    private int _indexInPlaylist;
    private IVirtualFileSystem? _vfs;

    private bool _isFullScreen;
    private FormBorderStyle _nonFullScreenBorderStyle;
    private FormWindowState _nonFullScreenWindowState;
    private Size _nonFullScreenSize;
    private bool _nonFullScreenControlBox;

    private CancellationTokenSource? _texFileLoadCancelTokenSource;
    private Task _navigationTask = Task.CompletedTask;

    public TextureViewer()
    {
        this.InitializeComponent();
        this.InitializeMenu();
        this._unconstrainedPanelWidth = this.LogicalToDeviceUnits(240);

        this._panelMouseTracker = new(this.PropertyPanel);
        this._panelMouseTracker.UseLeftDrag = true;
        this._panelMouseTracker.Pan += this.PanelMouseTrackerOnPan;

        this.PropertyPanel.Visible = false;
        this.PropertyPanel.VisibleChanged += this.PropertyPanelOnVisibleChanged;
        this.PropertyPanelGrid.PreviewKeyDown += this.PropertyPanelGridOnPreviewKeyDown;

        this.TexViewer.Margin = new(); // required line
        this.TexViewer.MouseActivity.MiddleClick += this.MouseActivityOnMiddleClick;
        this.TexViewer.PreviewKeyDown += this.TexViewerOnPreviewKeyDown;
        this.TexViewer.MouseDown += this.TexViewerOnMouseDown;
        this.TexViewer.NavigateToNextFile += this.TexViewerOnNavigateToNextFile;
        this.TexViewer.NavigateToPrevFile += this.TexViewerOnNavigateToPrevFile;
    }

    private void MouseActivityOnMiddleClick(Point cursor) => this.IsFullScreen = !this.IsFullScreen;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsFullScreen {
        get => this._isFullScreen;
        set {
            if (value == this._isFullScreen)
                return;

            using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

            if (!value) {
                this.ControlBox = this._nonFullScreenControlBox;
                this.FormBorderStyle = this._nonFullScreenBorderStyle;
                this.WindowState = this._nonFullScreenWindowState;
                this.Size = this._nonFullScreenSize;
            } else {
                this._nonFullScreenSize = this.Size;

                this._nonFullScreenControlBox = this.ControlBox;
                this.ControlBox = false;

                this._nonFullScreenBorderStyle = this.FormBorderStyle;
                this.FormBorderStyle = FormBorderStyle.None;

                this._nonFullScreenWindowState = this.WindowState;
                // Setting to normal then maximized is required to enter fullscreen, covering Windows task bar.
                this.WindowState = FormWindowState.Normal;
                this.WindowState = FormWindowState.Maximized;
            }

            this._isFullScreen = value;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData) {
            case Keys.S | Keys.Control:
                this.ShowSaveDialog();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void ShowSaveDialog()
    {
        if (this.TexViewer.CurrentBitmapSource is { } source &&
            this.TexViewer.BitmapSource?.FileName is { } fileName) {
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
                                    for (var k = 0; k < source.NumSlicesOfMipmap(i, j); k++) {
                                        using var d = File.Open(
                                            Path.Join(
                                                Path.GetDirectoryName(sfd.FileName),
                                                Path.ChangeExtension(
                                                    $"{Path.GetFileNameWithoutExtension(sfd.FileName)}.{i}.{j}.{k}._",
                                                    Path.GetExtension(sfd.FileName))),
                                            FileMode.Create,
                                            FileAccess.Write);
                                        var t = source.GetWicBitmapSourceAsync(i, j, k);
                                        t.Wait();

                                        t.Result.Save(
                                            d,
                                            sfd.FilterIndex switch {
                                                3 => GUID.GUID_ContainerFormatPng,
                                                4 => GUID.GUID_ContainerFormatJpeg,
                                                5 => GUID.GUID_ContainerFormatBmp,
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
    }

    private void TexViewerOnMouseDown(object? sender, MouseEventArgs e)
    {
        this._menuKeyPending = false;
        this.TexViewer.Focus();
    }

    private void PropertyPanelGridOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        switch (e.KeyCode) {
            case Keys.Tab:
                this.TogglePropertyGrid();
                break;
        }
    }

    private void TexViewerOnNavigateToPrevFile(object? sender, EventArgs eventArgs) => this.NavigateToPrevFile();

    private void TexViewerOnNavigateToNextFile(object? sender, EventArgs eventArgs) => this.NavigateToNextFile();

    private void NavigateToPrevFile() => this.Navigate(() => (this._indexInPlaylist - 1, -1));

    private void NavigateToNextFile() => this.Navigate(() => (this._indexInPlaylist + 1, 1));

    private void NavigateToFirstFile() => this.Navigate(() => (0, 1));

    private void NavigateToLastFile() => this.Navigate(() => (this._playlist.Count - 1, -1));

    private void Navigate(Func<(int Index, int Step)> target)
    {
        if (!this._navigationTask.IsCompleted)
            return;

        this._navigationTask = Task.Run(
            () => {
                var (index, step) = target();
                return this.FindAndSelectFirstTexFile(index, step, this._closeToken.Token);
            },
            this._closeToken.Token);
    }

    private void ToggleFullScreen(string keyName)
    {
        this.IsFullScreen = !this.IsFullScreen;
        if (this.IsFullScreen)
            this.TexViewer.ShowOverlayStringShort($"Press {keyName} key again to exit full screen mode.");
    }

    private void TexViewerOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        switch (e.KeyCode) {
            case Keys.Tab:
                this.TogglePropertyGrid();
                e.IsInputKey = true;
                break;
            case Keys.Enter:
                this.ToggleFullScreen("Enter");
                break;
            case Keys.F when e.Modifiers == Keys.None:
                this.ToggleFullScreen("F");
                break;
            case Keys.Escape:
                if (this.IsFullScreen) {
                    this.IsFullScreen = false;
                    this.TexViewer.ShowOverlayStringShort("Press Esc key again to close.");
                } else if (this.PropertyPanel.Visible)
                    this.TogglePropertyGrid();
                else
                    this.Close();

                break;
            case Keys.PageUp:
                this.NavigateToPrevFile();
                break;
            case Keys.PageDown:
                this.NavigateToNextFile();
                break;
            case Keys.Home:
                this.NavigateToFirstFile();
                break;
            case Keys.End:
                this.NavigateToLastFile();
                break;
        }
    }

    private void TogglePropertyGrid()
    {
        using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

        using (this.DisableRedrawScoped()) {
            var prev = this.RectangleToScreen(
                new(
                    this.TexViewer.Left + this.TexViewer.Margin.Left,
                    this.TexViewer.Top + this.TexViewer.Margin.Top,
                    this.TexViewer.Width - this.TexViewer.Margin.Horizontal,
                    this.TexViewer.Height - this.TexViewer.Margin.Vertical));

            this.PropertyPanel.Visible = !this.PropertyPanel.Visible;
            if (this.WindowState == FormWindowState.Normal) {
                if (this.PropertyPanel.Visible) {
                    var screen = Screen.FromControl(this);
                    var newWidth = Math.Min(this.Width + this.PropertyPanel.Width, screen.WorkingArea.Width);
                    var newLeft = this.Left + newWidth > screen.WorkingArea.Right
                        ? screen.WorkingArea.Right - newWidth
                        : this.Left;
                    this.SetBounds(newLeft, this.Top, newWidth, this.Height);
                    this.PropertyPanel.Focus();
                } else {
                    this.Width -= this.PropertyPanel.Width;
                    this.TexViewer.Focus();
                }
            }

            var curr = this.RectangleToScreen(
                new(
                    this.TexViewer.Left + this.TexViewer.Margin.Left,
                    this.TexViewer.Top + this.TexViewer.Margin.Top,
                    this.TexViewer.Width - this.TexViewer.Margin.Horizontal,
                    this.TexViewer.Height - this.TexViewer.Margin.Vertical));
            this.TexViewer.Pan = new(
                this.TexViewer.Pan.X + prev.X + prev.Width / 2f - curr.X - curr.Width / 2f,
                this.TexViewer.Pan.Y + prev.Y + prev.Height / 2f - curr.Y - curr.Height / 2f);
        }
    }

    public void SetFile(
        IVirtualFileSystem tree,
        IVirtualFile file,
        FileResource fileResource,
        IVirtualFolder? folder,
        IEnumerable<IVirtualFile> playlist)
    {
        this._vfs = tree;
        this._folder = folder;
        this._playlist.Clear();
        this._playlist.AddRange(
            playlist.Select(
                item => Tuple.Create(
                    item,
                    item.NameResolved && MultiBitmapViewerControl.MaySupportFileName(item.Name))));

        var fileTuple = Tuple.Create(file, true);
        this._indexInPlaylist = this._playlist.IndexOf(fileTuple);
        if (this._indexInPlaylist < 0) this._playlist.Insert(this._indexInPlaylist = 0, fileTuple);
        this.SelectFile(this._indexInPlaylist, fileResource);
    }

    private async Task<bool> FindAndSelectFirstTexFile(
        int index,
        int step,
        CancellationToken cancellationToken,
        bool cycle = true)
    {
        if (this._vfs is not { } tree || !this._playlist.Any())
            return false;

        var invalidIndices = new List<int>();
        FileResource? fileResource = null;
        for (; index >= 0 && index < this._playlist.Count; index += step) {
            var (item, confirmed) = this._playlist[index];

            if (confirmed) {
                await this.TexViewer.RunOnUiThread(
                    () => {
                        this.SelectFile(index, null);
                        if (cycle) this.TexViewer.ClearOverlayString();
                    });
                return true;
            }

            if (item.NameResolved) {
                if (!MultiBitmapViewerControl.MaySupportFileName(item.Name)) {
                    invalidIndices.Add(index);
                    continue;
                }
            }

            using var lookup = tree.GetLookup(item);
            if (lookup.Size < 0x80000000u) {
                try {
                    fileResource = await lookup.AsFileResource(cancellationToken);
                    if (MultiBitmapViewerControl.MaySupportFileResource(fileResource)) {
                        this._playlist[index] = Tuple.Create(item, true);
                        break;
                    }
                } catch (Exception) {
                    // pass
                }
            }

            fileResource = null;
            invalidIndices.Add(index);
        }

        if (invalidIndices.Any()) {
            if (step > 0) {
                foreach (var i in Enumerable.Reverse(invalidIndices)) this._playlist.RemoveAt(i);

                // All removed entries were before index; shift it so that it keeps pointing to the found file.
                index -= invalidIndices.Count;
            } else {
                foreach (var i in invalidIndices) this._playlist.RemoveAt(i);
            }
        }

        if (fileResource is not null) {
            await this.TexViewer.RunOnUiThread(
                () => {
                    this.SelectFile(index, fileResource);
                    if (cycle) this.TexViewer.ClearOverlayString();
                });
            return true;
        }

        if (this._playlist.Count == 0) {
            await this.TexViewer.RunOnUiThread(
                () => this.TexViewer.ShowOverlayStringLong("No vaild texture file could be found."));
            return false;
        }

        if (!cycle)
            return false;

        if (index < 0) {
            if (await this.FindAndSelectFirstTexFile(this._playlist.Count - 1, -1, cancellationToken, false)) {
                await this.TexViewer.RunOnUiThread(
                    () => this.TexViewer.ShowOverlayStringLong(
                        this._folder is null
                            ? "This is the last file in this search."
                            : "This is the last file in this folder."));
                return true;
            }

            return false;
        } else {
            if (await this.FindAndSelectFirstTexFile(0, 1, cancellationToken, false)) {
                await this.TexViewer.RunOnUiThread(
                    () => this.TexViewer.ShowOverlayStringLong(
                        this._folder is null
                            ? "This is the first file in this search."
                            : "This is the first file in this folder."));
                return true;
            }

            return false;
        }
    }

    private void SelectFile(int index, FileResource? fileResource)
    {
        if (this._vfs is not { } tree)
            return;
        this._indexInPlaylist = index;
        var (file, _) = this._playlist[index];
        this.Text = tree.GetFullPath(file);

        this._texFileLoadCancelTokenSource?.Cancel();
        this._texFileLoadCancelTokenSource = null;

        if (fileResource is not null) {
            this.TexViewer.SetFile(fileResource);
            this.PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(fileResource);
            return;
        }

        this._texFileLoadCancelTokenSource = new();

        using var lookup = tree.GetLookup(file);
        lookup.AsFileResource(this._texFileLoadCancelTokenSource.Token)
            .ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully || index != this._indexInPlaylist)
                        return;
                    this.TexViewer.SetFile(r.Result);
                    this.PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(r.Result);
                },
                this._texFileLoadCancelTokenSource.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void ShowRelativeTo(Control opener)
    {
        var rc = this.TexViewer.GetViewportRectangleSuggestion(opener);
        var minimumSize = this.LogicalToDeviceUnits(new Size(MinimumDefaultWidth, MinimumDefaultHeight));
        if (rc.Width < minimumSize.Width) {
            rc.X -= (minimumSize.Width - rc.Width) / 2;
            rc.Width = minimumSize.Width;
        }

        if (rc.Height < minimumSize.Height) {
            rc.Y -= (minimumSize.Height - rc.Height) / 2;
            rc.Height = minimumSize.Height;
        }

        this.SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        this.Show();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this._closeToken.Cancel();
        this._texFileLoadCancelTokenSource?.Cancel();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.WindowState == FormWindowState.Normal && this.IsFullScreen) this.IsFullScreen = false;
        this.ResizePanel(this._unconstrainedPanelWidth);
    }

    private void PropertyPanelOnVisibleChanged(object? sender, EventArgs e)
    {
        if (this.PropertyPanel.Visible)
            this.TexViewer.Margin = this.TexViewer.Margin with { Right = this.PropertyPanel.Width };
        else
            this.TexViewer.Margin = new();
    }

    private void PanelMouseTrackerOnPan(Point delta)
    {
        this.ResizePanel(this.PropertyPanel.Width - delta.X);
        this._unconstrainedPanelWidth = this.PropertyPanel.Width;
    }

    private void ResizePanel(int newSuggestedWidth)
    {
        var clientSize = this.ClientSize;
        var newPanelWidth = newSuggestedWidth;
        if (newPanelWidth > clientSize.Width)
            newPanelWidth = clientSize.Width;
        if (newPanelWidth < this.PropertyPanel.Padding.Horizontal)
            newPanelWidth = this.PropertyPanel.Padding.Horizontal;
        this.PropertyPanel.SetBounds(clientSize.Width - newPanelWidth, 0, newPanelWidth, clientSize.Height);
        if (this.PropertyPanel.Visible) this.TexViewer.Margin = this.TexViewer.Margin with { Right = newPanelWidth };
    }
}
