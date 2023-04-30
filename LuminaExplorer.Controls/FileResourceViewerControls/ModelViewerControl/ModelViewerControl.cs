using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using LuminaExplorer.Core.VirtualFileSystem;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;

public class ModelViewerControl : AbstractFileResourceViewerControl {
    private ResultDisposingTask<GamePixelShaderMdlRenderer>? _gameShaderRendererTask;
    private ResultDisposingTask<CustomMdlRenderer>? _customRendererTask;

    private Task<MdlRenderer>? _activeRendererTask;

    private CameraManager _cameraManager;

    private CancellationTokenSource? _modelTaskCancellationTokenSource;
    internal IVirtualFileSystem? Vfs;
    internal IVirtualFolder? VfsRoot;
    private Task<MdlFile>? _mdlFileTask;

    public ModelViewerControl() {
        base.BackColor = DefaultBackColor;
        _cameraManager = new(this);
        _cameraManager.ViewportChanged += OnCameraManagerOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _modelTaskCancellationTokenSource?.Cancel();
            _modelTaskCancellationTokenSource = null;
            _mdlFileTask = null;
            _ = SafeDispose.OneAsync(ref _cameraManager!);
            _ = SafeDispose.OneAsync(ref _customRendererTask!);
            _ = SafeDispose.OneAsync(ref _gameShaderRendererTask!);
            _ = SafeDispose.OneAsync(ref _customRendererTask!);
        }

        base.Dispose(disposing);
    }

    public event Action? ViewportChanged;

    public ICamera Camera => _cameraManager.Camera;

    public ObjectCentricCamera ObjectCentricCamera => _cameraManager.ObjectCentricCamera;

    public void SetModel(IVirtualFileSystem vfs, IVirtualFolder rootFolder, MdlFile mdlFile) {
        _modelTaskCancellationTokenSource?.Cancel();
        var cts = _modelTaskCancellationTokenSource = new();

        Vfs = vfs;
        VfsRoot = rootFolder;
        _mdlFileTask = Task.FromResult(mdlFile);

        //*
        _ = TryGetCustomRenderer(out _, true);
        _activeRendererTask = _customRendererTask?.Task.ContinueWith(r => (MdlRenderer)r.Result, cts.Token);
        /*/
        _ = TryGetGameShaderRenderer(out _, true);
        _activeRendererTask = _gameShaderRendererTask?.Task.ContinueWith(r => (MdlRenderer) r.Result, cts.Token);
        //*/

        _activeRendererTask!.ContinueWith(r => {
            if (r.IsCompletedSuccessfully)
                r.Result.UpdateModel(_mdlFileTask);
            Invalidate();
        }, cts.Token, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e) {
        if (!TryGetRenderer(out var renderer)) {
            base.OnPaintBackground(e);
            return;
        }

        renderer.Draw(e);
    }

    private void OnCameraManagerOnViewportChanged() {
        ViewportChanged?.Invoke();
        Invalidate();
    }

    private bool TryGetRenderer([MaybeNullWhen(false)] out MdlRenderer renderer) {
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

    internal Task<T?>? GetTypedFileAsync<T>(string path) where T : FileResource {
        if (Vfs is not { } vfs || VfsRoot is not { } vfsRoot || _modelTaskCancellationTokenSource?.Token is not { } cts)
            return null;
        return Task.Factory.StartNew(async () => {
            var file = await vfs.FindFile(vfsRoot, path);
            if (file is null)
                return null;

            using var lookup = vfs.GetLookup(file);
            return await lookup.AsFileResource<T>(cts);
        }, cts).Unwrap();
    }
}

public abstract unsafe class MdlRenderer : DirectXRenderer<ModelViewerControl> {
    protected MdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, true, pDevice, pDeviceContext) { }

    public abstract void ClearModel();

    public abstract void UpdateModel(Task<MdlFile> newModelTask);

    protected void ModelObjectOnDdsFileRequested(string path, ref Task<DdsFile?>? loader) {
        loader ??= Control.GetTypedFileAsync<TexFile>(path)?.ContinueWith(r =>
            !r.IsCompletedSuccessfully ? null : r.Result?.ToDdsFileFollowGameDx11Conversion());
    }

    protected void ModelObjectOnMtrlFileRequested(string path, ref Task<MtrlFile?>? loader) {
        loader ??= Control.GetTypedFileAsync<MtrlFile>(path);
    }

    protected void ModelObjectOnLoadStateChanged() {
        Control.Invalidate();
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        Span<float> colors = stackalloc float[4];
        colors[0] = 1f * Control.BackColor.R / 255;
        colors[1] = 1f * Control.BackColor.G / 255;
        colors[2] = 1f * Control.BackColor.B / 255;
        colors[3] = 1f * Control.BackColor.A / 255;

        DeviceContext->ClearRenderTargetView(pRenderTarget, ref colors[0]);
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget) {
        // empty
    }
}

public unsafe class GamePixelShaderMdlRenderer : MdlRenderer {
    private GameShaderPool _pool;
    private Task<MdlFile>? _modelTask;
    private ResultDisposingTask<ModelObjectWithGameShader>? _modelObject;
    private GameShaderState _shaderState;

    public GamePixelShaderMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public GamePixelShaderMdlRenderer(
        ModelViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext) {
        _pool = new(Device, DeviceContext);
        _pool.ShpkFileRequested += PoolOnShpkFileRequested;
        _shaderState = new(_pool);
        Control.ViewportChanged += ControlOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ClearModel();
            _ = SafeDispose.OneAsync(ref _shaderState!);
            _ = SafeDispose.OneAsync(ref _pool!);
        }

        base.Dispose(disposing);
    }

    public override void ClearModel() {
        _modelObject?.Task.ContinueWith(r => {
            if (r.IsCompletedSuccessfully) {
                var modelObject = r.Result;
                modelObject.DdsFileRequested -= ModelObjectOnDdsFileRequested;
                modelObject.MtrlFileRequested -= ModelObjectOnMtrlFileRequested;
                modelObject.ResourceLoadStateChanged -= ModelObjectOnLoadStateChanged;
            }
        });
        _ = SafeDispose.OneAsync(ref _modelObject);
        _ = SafeDispose.OneAsync(ref _shaderState!);
        _modelTask = null;
    }

    public override void UpdateModel(Task<MdlFile> newModelTask) {
        if (_modelTask == newModelTask)
            return;

        ClearModel();
        _modelTask = newModelTask;

        _modelObject = new(newModelTask.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully)
                throw r.Exception!;

            var modelObject = new ModelObjectWithGameShader(_pool, r.Result);
            modelObject.DdsFileRequested += ModelObjectOnDdsFileRequested;
            modelObject.MtrlFileRequested += ModelObjectOnMtrlFileRequested;
            modelObject.ResourceLoadStateChanged += ModelObjectOnLoadStateChanged;
            return modelObject;
        }, TaskScheduler.FromCurrentSynchronizationContext()));
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        base.Draw3D(pRenderTarget);
        // TODO
        if (_modelObject?.IsCompletedSuccessfully is true)
            _modelObject.Result.Draw(_shaderState);
    }

    private void PoolOnShpkFileRequested(string path, ref Task<ShpkFile?>? loader) => 
        loader ??= Control.GetTypedFileAsync<ShpkFile>(path);

    private void ControlOnViewportChanged() {
        // _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
    }
}

public unsafe class CustomMdlRenderer : MdlRenderer {
    private CustomMdlRendererShader _shader;
    private CustomMdlRendererShader.State _shaderState;
    private Task<MdlFile>? _modelTask;
    private Task<CustomMdlRendererShader.ModelObject>? _modelObject;

    public CustomMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public CustomMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext) {
        _shader = new(Device, DeviceContext);
        _shaderState = new(Device, DeviceContext);
        Control.ViewportChanged += ControlOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ClearModel();
            _ = SafeDispose.OneAsync(ref _shader!);
            _ = SafeDispose.OneAsync(ref _shaderState!);
        }

        base.Dispose(disposing);
    }

    public override void ClearModel() {
        _modelTask = null;
        _modelObject?.ContinueWith(r => {
            if (r.IsCompletedSuccessfully) {
                var modelObject = r.Result;
                modelObject.DdsFileRequested -= ModelObjectOnDdsFileRequested;
                modelObject.TextureLoadStateChanged -= ModelObjectOnLoadStateChanged;
                modelObject.MtrlFileRequested -= ModelObjectOnMtrlFileRequested;
            }
        });
        _modelObject = null;
    }

    public override void UpdateModel(Task<MdlFile> newModelTask) {
        if (_modelTask == newModelTask)
            return;

        ClearModel();
        _modelTask = newModelTask;

        newModelTask.ContinueWith(r => {
            if (_modelTask != newModelTask)
                return;

            _modelObject = Task.Run(() => {
                var modelObject = new CustomMdlRendererShader.ModelObject(_shader, r.Result);
                modelObject.DdsFileRequested += ModelObjectOnDdsFileRequested;
                modelObject.TextureLoadStateChanged += ModelObjectOnLoadStateChanged;
                modelObject.MtrlFileRequested += ModelObjectOnMtrlFileRequested;
                return modelObject;
            });
            Control.RunOnUiThreadAfter(_modelObject, _ => {
                if (_modelTask != newModelTask)
                    return;

                var bb = _modelTask.Result.ModelBoundingBoxes;
                var target = new Vector3(
                    (bb.Min[0] + bb.Max[0]) / 2f,
                    (bb.Min[1] + bb.Max[1]) / 2f,
                    (bb.Min[2] + bb.Max[2]) / 2f);
                var occ = Control.ObjectCentricCamera;
                occ.Update(
                    target: target,
                    yawFromTarget: 0,
                    pitchFromTarget: 0,
                    distance: 64 * (
                        target.X * occ.System.Forward.X +
                        target.Y + occ.System.Forward.Y +
                        target.Z + occ.System.Forward.Z));
                _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
                Control.Invalidate();
            });
        });
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        base.Draw3D(pRenderTarget);
        if (_modelObject?.IsCompletedSuccessfully is true)
            _shader.Draw(_shaderState, _modelObject.Result);
    }

    private void ControlOnViewportChanged() {
        _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
    }
}

public sealed class CameraManager : IDisposable {
    public readonly System3D System3D;

    private readonly AbstractFileResourceViewerControl _control;

    public CameraManager(AbstractFileResourceViewerControl control, System3D? system3D = null) {
        System3D = system3D ?? new(new(0, 0, -1), new(0, 1, 0), new(1, 0, 0));
        _control = control;
        ObjectCentricCamera = new(System3D);
        _control.MouseActivity.UseLeftDrag = true;
        _control.MouseActivity.UseInfiniteLeftDrag = true;
        _control.MouseActivity.UseRightDrag = true;
        _control.MouseActivity.UseInfiniteRightDrag = true;
        _control.MouseActivity.UseWheelZoom = MouseActivityTracker.WheelZoomMode.Always;
        _control.MouseActivity.Pan += MouseActivityOnPan;
        _control.MouseActivity.ZoomWheel += MouseActivityOnZoomWheel;
        _control.ClientSizeChanged += ControlOnClientSizeChanged;
    }

    public void Dispose() {
        _control.MouseActivity.Pan -= MouseActivityOnPan;
        _control.ClientSizeChanged -= ControlOnClientSizeChanged;
    }

    public event Action? ViewportChanged;

    public ObjectCentricCamera ObjectCentricCamera { get; }

    public ICamera Camera => ObjectCentricCamera;

    private void MouseActivityOnPan(Point delta) {
        if (_control.MouseActivity.FirstHeldButton == MouseButtons.Left) {
            ObjectCentricCamera.YawFromTarget += delta.X * MathF.PI / 720;
            var pitch = ObjectCentricCamera.PitchFromTarget + delta.Y * MathF.PI / 720;
            ObjectCentricCamera.PitchFromTarget = Math.Clamp(pitch, MathF.PI / -2, MathF.PI / 2);
        } else if (_control.MouseActivity.FirstHeldButton == MouseButtons.Right) {
            ObjectCentricCamera.Target += System3D.Up * delta.Y / 12f;
        }

        ViewportChanged?.Invoke();
    }

    private void MouseActivityOnZoomWheel(Point origin, int delta) {
        ObjectCentricCamera.Distance = MathF.Max(ObjectCentricCamera.Distance + delta / 12f, 1f);
        ViewportChanged?.Invoke();
    }

    private void ControlOnClientSizeChanged(object? sender, EventArgs e) {
        ObjectCentricCamera.Viewport = new(_control.ClientSize.Width, _control.ClientSize.Height);
        ViewportChanged?.Invoke();
    }
}

public readonly struct System3D {
    public readonly Vector3 Forward;
    public readonly Vector3 Up;
    public readonly Vector3 Right;

    public System3D(Vector3 forward, Vector3 up, Vector3 right) {
        Forward = forward;
        Up = up;
        Right = right;
    }
}

public interface ICamera {
    public Matrix4x4 View { get; }

    public Matrix4x4 Projection { get; }
}

public class ObjectCentricCamera : ICamera {
    public readonly System3D System;

    private Vector3 _target = Vector3.Zero;
    private float _yawFromTarget;
    private float _pitchFromTarget;
    private float _distance = 1f;
    private Vector2 _viewport = Vector2.One;

    private Matrix4x4? _view;
    private Matrix4x4? _projection;

    public ObjectCentricCamera(System3D system) {
        System = system;
    }

    public ObjectCentricCamera(Vector3 bboxMin, Vector3 bboxMax, System3D system) {
        System = system;
        var target = (bboxMin + bboxMax) / 2;
        Update(
            target: target,
            yawFromTarget: 0,
            pitchFromTarget: 0,
            distance: 64);
    }

    public Vector3 Target {
        get => _target;
        set => Update(target: value);
    }

    public Vector2 Viewport {
        get => _viewport;
        set => Update(viewport: value);
    }

    public float YawFromTarget {
        get => _yawFromTarget;
        set => Update(yawFromTarget: value);
    }

    public float PitchFromTarget {
        get => _pitchFromTarget;
        set => Update(pitchFromTarget: value);
    }

    public float Distance {
        get => _distance;
        set => Update(distance: value);
    }

    public Matrix4x4 View {
        get {
            if (_view is null) {
                var rotation = Matrix4x4.CreateFromYawPitchRoll(-_yawFromTarget, _pitchFromTarget, 0);
                var scaledDistance = MathF.Pow(2, _distance / 64f);
                _view = Matrix4x4.CreateLookAt(
                    cameraPosition: Vector3.Transform(System.Forward * scaledDistance, rotation),
                    cameraTarget: _target,
                    cameraUpVector: System.Up);
            }

            return _view.Value;
        }
    }

    public Matrix4x4 Projection => _projection ??=
        Matrix4x4.CreatePerspectiveFieldOfView(0.9f, _viewport.X / _viewport.Y, 0.1f, 10000.0f);

    public void Update(
        Vector3? target = null,
        Vector2? viewport = null,
        float? yawFromTarget = null,
        float? pitchFromTarget = null,
        float? distance = null) {
        _target = target ?? _target;
        _viewport = viewport ?? _viewport;
        _yawFromTarget = yawFromTarget ?? _yawFromTarget;
        _pitchFromTarget = pitchFromTarget ?? _pitchFromTarget;
        _distance = distance ?? _distance;

        _view = null;
        _projection = null;
    }
}
