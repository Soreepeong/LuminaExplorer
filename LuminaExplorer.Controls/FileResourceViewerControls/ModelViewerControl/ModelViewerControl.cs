using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;
using LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;

public class ModelViewerControl : AbstractFileResourceViewerControl {
    private ResultDisposingTask<GamePixelShaderMdlRenderer>? _gameShaderRendererTask;
    private ResultDisposingTask<CustomMdlRenderer>? _customRendererTask;

    private Task<BaseMdlRenderer>? _activeRendererTask;

    private CameraManager _cameraManager;

    internal IVirtualFileSystem? Vfs;
    internal IVirtualFolder? VfsRoot;
    private CancellationTokenSource? _mdlCancel;
    private Task<MdlFile>? _mdlFileTask;
    private CancellationTokenSource? _animationCancel;
    private Task<IAnimation>[]? _animationTasks;
    private float _animationSpeed = 1f;
    private bool _animationPlaying = true;

    public ModelViewerControl() {
        base.BackColor = DefaultBackColor;
        _cameraManager = new(this);
        _cameraManager.ViewportChanged += OnCameraManagerOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _mdlCancel?.Cancel();
            _mdlCancel = null;
            _animationCancel?.Cancel();
            _animationCancel = null;
            _mdlFileTask = null;
            _ = SafeDispose.OneAsync(ref _cameraManager!);
            _ = SafeDispose.OneAsync(ref _customRendererTask!);
            _ = SafeDispose.OneAsync(ref _gameShaderRendererTask!);
            _ = SafeDispose.OneAsync(ref _customRendererTask!);
        }

        base.Dispose(disposing);
    }

    public event EventHandler? ViewportChanged;

    public event EventHandler? AnimationPlayingChanged; 

    public event EventHandler? AnimationSpeedChanged;

    public ICamera Camera => _cameraManager.Camera;

    public ObjectCentricCamera ObjectCentricCamera => _cameraManager.ObjectCentricCamera;

    public void SetModel(IVirtualFileSystem vfs, IVirtualFolder rootFolder, Task<MdlFile> mdlFileTask) {
        if (_mdlFileTask == mdlFileTask)
            return;

        _mdlCancel?.Cancel();
        var cts = _mdlCancel = new();

        Vfs = vfs;
        VfsRoot = rootFolder;
        _mdlFileTask = mdlFileTask;

        ModelInfoResolverTask ??= ModelInfoResolver.GetResolver(GetTypedFileAsync<EstFile>);
        //*
        _ = TryGetCustomRenderer(out _, true);
        _activeRendererTask = _customRendererTask?.Task.ContinueWith(r => (BaseMdlRenderer) r.Result, cts.Token);
        /*/
        _ = TryGetGameShaderRenderer(out _, true);
        _activeRendererTask = _gameShaderRendererTask?.Task.ContinueWith(r => (MdlRenderer) r.Result, cts.Token);
        //*/

        _activeRendererTask!.ContinueWith(r => {
            if (r.IsCompletedSuccessfully)
                r.Result.SetModel(_mdlFileTask);
            Invalidate();
        }, cts.Token, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public bool AnimationPlaying {
        get => _animationPlaying;
        set {
            if (_animationPlaying == value)
                return;
            _animationPlaying = value;
            AnimationPlayingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public float AnimationSpeed {
        get => _animationSpeed;
        set {
            if (Equals(_animationSpeed, value))
                return;
            _animationSpeed = value;
            AnimationSpeedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task<IAnimation>[]? Animations {
        get => _animationTasks;
        set {
            if (value == _animationTasks)
                return;

            _animationCancel?.Cancel();
            _animationCancel = null;
            if (value is null)
                return;

            _ = TryGetCustomRenderer(out _, true);
            var cts = _animationCancel = new();
            _animationTasks = value;
            _activeRendererTask!.ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully || _animationTasks != value)
                        return;

                    r.Result.SetAnimations(value);
                    Invalidate();
                },
                cts.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    public Task<ModelInfoResolver>? ModelInfoResolverTask { get; set; }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e) {
        if (!TryGetRenderer(out var renderer)) {
            base.OnPaintBackground(e);
            return;
        }

        renderer.Draw(e);
    }

    private void OnCameraManagerOnViewportChanged() {
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private bool TryGetRenderer([MaybeNullWhen(false)] out BaseMdlRenderer renderer) {
        if (_activeRendererTask is null) {
            renderer = null!;
            return false;
        }

        if (_activeRendererTask?.IsCompletedSuccessfully is true) {
            renderer = _activeRendererTask.Result;
            return true;
        }

        renderer = null!;
        return false;
    }

    public bool TryGetCustomRenderer(
        [MaybeNullWhen(false)] out CustomMdlRenderer renderer,
        bool startInitializing = false) {
        if (_customRendererTask?.IsCompletedSuccessfully is true) {
            renderer = _customRendererTask.Result;
            return true;
        }

        renderer = null!;
        if (!startInitializing)
            return false;

        _customRendererTask ??= new(Task
            .Run(() => new CustomMdlRenderer(this))
            .ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.UiThreadInitialize();
                return r.Result;
            }, UiTaskScheduler));
        return false;
    }

    public bool TryGetGameShaderRenderer(
        [MaybeNullWhen(false)] out GamePixelShaderMdlRenderer renderer,
        bool startInitializing = false) {
        if (_gameShaderRendererTask?.IsCompletedSuccessfully is true) {
            renderer = _gameShaderRendererTask.Result;
            return true;
        }

        renderer = null!;
        if (!startInitializing)
            return false;

        _gameShaderRendererTask ??= new(Task
            .Run(() => new GamePixelShaderMdlRenderer(this))
            .ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.UiThreadInitialize();
                return r.Result;
            }, UiTaskScheduler));
        return false;
    }

    internal Task<T?> GetTypedFileAsync<T>(string path) where T : FileResource {
        if (Vfs is not { } vfs || VfsRoot is not { } vfsRoot || _mdlCancel?.Token is not { } cts)
            return Task.FromResult((T?) null);
        return Task.Factory.StartNew(async () => {
            var file = await vfs.LocateFile(vfsRoot, path);
            if (file is null)
                return null;

            using var lookup = vfs.GetLookup(file);
            return await lookup.AsFileResource<T>(cts);
        }, cts).Unwrap();
    }
}
