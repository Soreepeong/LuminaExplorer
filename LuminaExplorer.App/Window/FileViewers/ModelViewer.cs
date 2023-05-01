using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class ModelViewer : Form {
    private const int MinimumDefaultWidth = 320;
    private const int MinimumDefaultHeight = 240;

    private readonly MouseActivityTracker _panelMouseTracker;
    private int _unconstrainedPanelWidth = 240;

    private readonly CancellationTokenSource _closeToken = new();

    private bool _isFullScreen;
    private FormBorderStyle _nonFullScreenBorderStyle;
    private FormWindowState _nonFullScreenWindowState;
    private Size _nonFullScreenSize;
    private bool _nonFullScreenControlBox;

    private CancellationTokenSource? _loadCancelTokenSource;
    
    private readonly List<Task<IAnimation>> _animations = new();
    private int _currentAnimationIndex = -1;

    public ModelViewer() {
        InitializeComponent();

        _panelMouseTracker = new(PropertyPanel);
        _panelMouseTracker.UseLeftDrag = true;
        _panelMouseTracker.Pan += PanelMouseTrackerOnPan;

        PropertyPanel.Visible = false;
        PropertyPanel.VisibleChanged += PropertyPanelOnVisibleChanged;
        PropertyPanelGrid.PreviewKeyDown += PropertyPanelGridOnPreviewKeyDown;

        Viewer.Margin = new(); // required line
        Viewer.MouseActivity.MiddleClick += MouseActivityOnMiddleClick;
        Viewer.PreviewKeyDown += ViewerOnPreviewKeyDown;
        Viewer.MouseDown += ViewerOnMouseDown;
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

    private void ViewerOnMouseDown(object? sender, MouseEventArgs e) {
        Viewer.Focus();
    }

    private void PropertyPanelGridOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Tab:
                TogglePropertyGrid();
                break;
        }
    }

    private void ViewerOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.PageUp:
                if (_animations.Any())
                    Viewer.Animation = _animations[MiscUtils.PositiveMod(--_currentAnimationIndex, _animations.Count)];
                break;
            case Keys.PageDown:
                if (_animations.Any())
                    Viewer.Animation = _animations[MiscUtils.PositiveMod(++_currentAnimationIndex, _animations.Count)];
                break;
            case Keys.Tab:
                TogglePropertyGrid();
                e.IsInputKey = true;
                break;
            case Keys.Enter:
                IsFullScreen = !IsFullScreen;
                break;
            case Keys.Escape:
                if (IsFullScreen)
                    IsFullScreen = false;
                else if (PropertyPanel.Visible)
                    TogglePropertyGrid();
                else
                    Close();

                break;
        }
    }

    private void TogglePropertyGrid() {
        using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

        using (this.DisableRedrawScoped()) {
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
                    Viewer.Focus();
                }
            }
        }
    }

    public void SetFile(IVirtualFileSystem vfs, IVirtualFolder root, IVirtualFile file, MdlFile? mdlFile) {
        Text = vfs.GetFullPath(file);

        _loadCancelTokenSource?.Cancel();
        _loadCancelTokenSource = null;

        Task<MdlFile> task;
        var cts = _loadCancelTokenSource = new();
        if (mdlFile is not null) {
            task = Task.FromResult(mdlFile);
        } else {
            task = Task.Factory.StartNew(async () => {
                using var lookup = vfs.GetLookup(file);
                return await lookup.AsFileResource<MdlFile>(cts.Token);
            }, cts.Token).Unwrap();
        }

        Viewer.SetModel(vfs, root, task);
        task.ContinueWith(
            r => {
                if (!r.IsCompletedSuccessfully)
                    return;
                PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(r.Result);
            },
            _loadCancelTokenSource.Token,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());
        
        _animations.Clear();
        _currentAnimationIndex = -1;
        task.ContinueWith(async r => {
            if (!r.IsCompletedSuccessfully)
                return;

            mdlFile = r.Result;

            if (file.Parent.Parent?.Parent?.Parent?.Parent is { } modelBaseFolder &&
                await vfs.LocateFolder(modelBaseFolder, "animation/") is { } animationFolder) {
                void OnFileFound(IVirtualFile papFile) {
                    using var lookup = vfs.GetLookup(papFile);
                    var papTask = lookup.AsFileResource<PapFile>(cts.Token);
                    Viewer.RunOnUiThreadAfter(papTask, r2 => {
                        foreach (var animation in r2.Result.AnimationBindings) {
                            var animationTask = Task.FromResult((IAnimation)animation);
                            _animations.Add(animationTask);
                            if (Viewer.Animation is null) {
                                _currentAnimationIndex = 0;
                                Viewer.Animation = animationTask;
                            }
                        }
                    });
                }

                await Task.WhenAll(
                    vfs.Search(animationFolder, "*.pap", null, null, OnFileFound, cancellationToken: cts.Token),
                    Viewer.ModelInfoResolverTask!.ContinueWith(async ra => {
                        if (!ra.IsCompletedSuccessfully)
                            return;
                        
                        var charaFolder = await vfs.LocateFolder(root, "chara/");
                        if (charaFolder is null)
                            return;

                        if (!ra.Result.TryFindSklbPath(mdlFile!.FilePath.Path, out var sklbPath))
                            return;

                        var sklbFile = await vfs.LocateFile(root, sklbPath);
                        if (sklbFile is null)
                            return;
                        SklbFile sklb;
                        using (var lookup = vfs.GetLookup(sklbFile))
                            sklb = await lookup.AsFileResource<SklbFile>(cts.Token);
                        
                        foreach (var f in vfs.GetFolders(await vfs.AsFoldersResolved(charaFolder))) {
                            if (f is not SqpackFolder {IsUnknownContainer: true} unknownFolder)
                                continue;
                            
                            // brute force paps
                            foreach (var f2 in vfs.GetFolders(await vfs.AsFoldersResolved(unknownFolder))) {
                                foreach (var f3 in vfs.GetFiles(f2)) {
                                    cts.Token.ThrowIfCancellationRequested();
                                    try {
                                        using var lookup = vfs.GetLookup(f3);
                                        var papTask = lookup.AsFileResource<PapFile>(cts.Token);
                                        await Viewer.RunOnUiThreadAfter(papTask, r2 => {
                                            if (!papTask.IsCompletedSuccessfully)
                                                return;
                                            if (papTask.Result.Header.ModelClassification !=
                                                sklb.VersionedHeader.ModelClassification)
                                                return;
                                            if (papTask.Result.Header.ModelId != sklb.VersionedHeader.ModelId)
                                                return;
                                            foreach (var animation in r2.Result.AnimationBindings) {
                                                var animationTask = Task.FromResult((IAnimation) animation);
                                                _animations.Add(animationTask);
                                                if (Viewer.Animation is null) {
                                                    _currentAnimationIndex = 0;
                                                    Viewer.Animation = animationTask;
                                                }
                                            }
                                        });
                                    } catch (Exception e) when (e is not OperationCanceledException) {
                                        // pass
                                    }
                                }
                            }
                        }
                    }, cts.Token));
            }
        }, cts.Token);
    }

    public void ShowRelativeTo(Control opener) {
        var rc = Viewer.GetViewportRectangleSuggestion(opener);
        if (rc.Width < MinimumDefaultWidth) {
            rc.X -= (MinimumDefaultWidth - rc.Width) / 2;
            rc.Width = MinimumDefaultWidth;
        }

        if (rc.Height < MinimumDefaultHeight) {
            rc.X -= (MinimumDefaultHeight - rc.Height) / 2;
            rc.Height = MinimumDefaultHeight;
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
            Viewer.Margin = Viewer.Margin with {Right = PropertyPanel.Width};
        else
            Viewer.Margin = new();
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
            Viewer.Margin = Viewer.Margin with {Right = newPanelWidth};
    }
}
