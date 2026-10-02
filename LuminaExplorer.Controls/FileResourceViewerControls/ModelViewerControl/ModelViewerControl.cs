using System;
using System.ComponentModel;
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
    private ResultDisposingTask<GameShaderMdlRenderer>? _gameShaderRendererTask;
    private ResultDisposingTask<CustomMdlRenderer>? _customRendererTask;

    private Task<BaseMdlRenderer>? _activeRendererTask;

    private CameraManager _cameraManager;
    private CancellationTokenSource? _mdlCancel;
    private Task<MdlFile>? _mdlFileTask;
    private CancellationTokenSource? _animationCancel;
    private Task<IAnimation>[]? _animationTasks;
    private float _animationSpeed = 1f;
    private bool _animationPlaying = true;
    private ModelRendererType _rendererType = ModelRendererType.Custom;

    public ModelViewerControl()
    {
        base.BackColor = DefaultBackColor;
        this._cameraManager = new(this);
        this._cameraManager.ViewportChanged += this.OnCameraManagerOnViewportChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._mdlCancel?.Cancel();
            this._mdlCancel = null;
            this._animationCancel?.Cancel();
            this._animationCancel = null;
            this._mdlFileTask = null;
            _ = SafeDispose.OneAsync(ref this._cameraManager!);
            _ = SafeDispose.OneAsync(ref this._customRendererTask!);
            _ = SafeDispose.OneAsync(ref this._gameShaderRendererTask!);
        }

        base.Dispose(disposing);
    }

    public event EventHandler? ViewportChanged;

    public event EventHandler? AnimationPlayingChanged;

    public event EventHandler? AnimationSpeedChanged;

    public event EventHandler? RendererTypeChanged;

    public IVirtualFileSystem? Vfs { get; private set; }

    public IVirtualFolder? VfsRoot { get; private set; }

    public ICamera Camera => this._cameraManager.Camera;

    public ObjectCentricCamera ObjectCentricCamera => this._cameraManager.ObjectCentricCamera;

    public Task<MdlFile>? ModelTask => this.TryGetRenderer(out var renderer) ? renderer.ModelTask : null;

    public Task<SklbFile[]>? SkeletonTask => this.TryGetRenderer(out var renderer) ? renderer.SkeletonTask : null;

    public void SetModel(IVirtualFileSystem vfs, IVirtualFolder rootFolder, Task<MdlFile> mdlFileTask)
    {
        if (this._mdlFileTask == mdlFileTask)
            return;

        this._mdlCancel?.Cancel();
        var cts = this._mdlCancel = new();

        this.Vfs = vfs;
        this.VfsRoot = rootFolder;
        this._mdlFileTask = mdlFileTask;

        this.ModelInfoResolverTask ??= ModelInfoResolver.GetResolver(
            this.GetTypedFileAsync<EstFile>,
            this.GetTypedFileAsync<PbdFile>);
        this.ActivateRenderer();
    }

    /// <summary>Gets or sets the renderer to use.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    [DefaultValue(ModelRendererType.Custom)]
    public ModelRendererType RendererType {
        get => this._rendererType;
        set {
            if (this._rendererType == value)
                return;
            this._rendererType = value;
            if (this._mdlFileTask is not null)
                this.ActivateRenderer();
            this.RendererTypeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gets the active renderer, if it has been initialized.</summary>
    public BaseMdlRenderer? ActiveRenderer => this.TryGetRenderer(out var renderer) ? renderer : null;

    private void ActivateRenderer()
    {
        if (this._mdlCancel?.Token is not { } token)
            return;

        // Only one renderer may present to this control at a time; dispose the other one.
        if (this._rendererType == ModelRendererType.GameShaders) {
            _ = SafeDispose.OneAsync(ref this._customRendererTask);
            _ = this.TryGetGameShaderRenderer(out _, true);
            this._activeRendererTask =
                this._gameShaderRendererTask?.Task.ContinueWith(r => (BaseMdlRenderer) r.Result, token);
        } else {
            _ = SafeDispose.OneAsync(ref this._gameShaderRendererTask);
            _ = this.TryGetCustomRenderer(out _, true);
            this._activeRendererTask =
                this._customRendererTask?.Task.ContinueWith(r => (BaseMdlRenderer) r.Result, token);
        }

        var activeRendererTask = this._activeRendererTask!;
        activeRendererTask.ContinueWith(
            r => {
                if (r.IsCompletedSuccessfully && this._activeRendererTask == activeRendererTask) {
                    r.Result.ModelTask = this._mdlFileTask;
                    r.Result.AnimationsTask = this._animationTasks;
                }

                this.Invalidate();
            },
            token,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool AnimationPlaying {
        get => this._animationPlaying;
        set {
            if (this._animationPlaying == value)
                return;
            this._animationPlaying = value;
            this.AnimationPlayingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public float AnimationSpeed {
        get => this._animationSpeed;
        set {
            if (Equals(this._animationSpeed, value))
                return;
            this._animationSpeed = value;
            this.AnimationSpeedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Task<IAnimation>[]? Animations {
        get => this._animationTasks;
        set {
            if (value == this._animationTasks)
                return;

            this._animationCancel?.Cancel();
            this._animationCancel = null;
            this._animationTasks = value;
            if (value is null) {
                if (this.TryGetRenderer(out var renderer))
                    renderer.AnimationsTask = null;
                return;
            }

            var cts = this._animationCancel = new();
            this._activeRendererTask?.ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully || this._animationTasks != value)
                        return;

                    r.Result.AnimationsTask = value;
                    this.Invalidate();
                },
                cts.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Task<ModelInfoResolver>? ModelInfoResolverTask { get; set; }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!this.TryGetRenderer(out var renderer)) {
            base.OnPaintBackground(e);
            return;
        }

        renderer.Draw(e);
    }

    private void OnCameraManagerOnViewportChanged()
    {
        this.ViewportChanged?.Invoke(this, EventArgs.Empty);
        this.Invalidate();
    }

    private bool TryGetRenderer([MaybeNullWhen(false)] out BaseMdlRenderer renderer)
    {
        if (this._activeRendererTask is null) {
            renderer = null!;
            return false;
        }

        if (this._activeRendererTask?.IsCompletedSuccessfully is true) {
            renderer = this._activeRendererTask.Result;
            return true;
        }

        renderer = null!;
        return false;
    }

    public bool TryGetCustomRenderer(
        [MaybeNullWhen(false)] out CustomMdlRenderer renderer,
        bool startInitializing = false)
    {
        if (this._customRendererTask?.IsCompletedSuccessfully is true) {
            renderer = this._customRendererTask.Result;
            return true;
        }

        renderer = null!;
        if (!startInitializing)
            return false;

        this._customRendererTask ??= new(
            Task
                .Run(() => new CustomMdlRenderer(this))
                .ContinueWith(
                    r => {
                        if (r.IsCompletedSuccessfully)
                            r.Result.UiThreadInitialize();
                        return r.Result;
                    },
                    this.UiTaskScheduler));
        return false;
    }

    public bool TryGetGameShaderRenderer(
        [MaybeNullWhen(false)] out GameShaderMdlRenderer renderer,
        bool startInitializing = false)
    {
        if (this._gameShaderRendererTask?.IsCompletedSuccessfully is true) {
            renderer = this._gameShaderRendererTask.Result;
            return true;
        }

        renderer = null!;
        if (!startInitializing)
            return false;

        this._gameShaderRendererTask ??= new(
            Task
                .Run(() => new GameShaderMdlRenderer(this))
                .ContinueWith(
                    r => {
                        if (r.IsCompletedSuccessfully)
                            r.Result.UiThreadInitialize();
                        return r.Result;
                    },
                    this.UiTaskScheduler));
        return false;
    }

    internal Task<T?> GetTypedFileAsync<T>(string path) where T : FileResource
    {
        if (this.Vfs is not { } vfs || this.VfsRoot is not { } vfsRoot || this._mdlCancel?.Token is not { } cts)
            return Task.FromResult((T?) null);
        return Task.Factory.StartNew(
            async () => {
                var file = await vfs.LocateFile(vfsRoot, path);
                if (file is null)
                    return null;

                using var lookup = vfs.GetLookup(file);
                return await lookup.AsFileResource<T>(cts);
            },
            cts).Unwrap();
    }
}

public enum ModelRendererType {
    /// <summary>Renders using a simple shader made for this viewer, supporting animations.</summary>
    Custom,

    /// <summary>Renders using the shaders shipped with the game.</summary>
    GameShaders,
}
