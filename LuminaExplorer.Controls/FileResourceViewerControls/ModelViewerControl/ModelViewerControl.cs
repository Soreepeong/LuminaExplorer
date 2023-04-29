using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using LuminaExplorer.Core.VirtualFileSystem;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;

public class ModelViewerControl : AbstractFileResourceViewerControl {
    private ResultDisposingTask<MdlRenderer>? _rendererTask;
    private CameraManager _cameraManager;

    private CancellationTokenSource? _modelTaskCancellationTokenSource;
    private Task<Model>? _modelTask;

    public ModelViewerControl() {
        base.BackColor = DefaultBackColor;
        _cameraManager = new(this);
        _cameraManager.ViewportChanged += OnCameraManagerOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _modelTaskCancellationTokenSource?.Cancel();
            _modelTaskCancellationTokenSource = null;
            _modelTask = null;
            _ = SafeDispose.OneAsync(ref _cameraManager!);
            _ = SafeDispose.OneAsync(ref _rendererTask!);
        }

        base.Dispose(disposing);
    }

    public event Action? ViewportChanged;

    public ICamera Camera => _cameraManager.Camera;

    public ObjectCentricCamera ObjectCentricCamera => _cameraManager.ObjectCentricCamera;

    public void SetModel(IVirtualFileSystem vfs, IVirtualFolder rootFolder, MdlFile mdlFile) {
        _modelTaskCancellationTokenSource?.Cancel();
        var cts = _modelTaskCancellationTokenSource = new();
        _modelTask = Task.Factory.StartNew(async () => {
            var model = new Model(mdlFile);
            for (var i = 0; i < model.Materials.Length; i++) {
                cts.Token.ThrowIfCancellationRequested();
                var m = model.Materials[i];
                if (m.File is not null)
                    continue;
                var materialPath = (string?) m.MaterialPath;
                if (materialPath?.StartsWith("/") is true)
                    materialPath = Material.ResolveRelativeMaterialPath(materialPath, m.VariantId);
                if (materialPath is null)
                    continue;
                if (await vfs.FindFile(rootFolder, materialPath) is not { } file)
                    continue;
                cts.Token.ThrowIfCancellationRequested();
                using var lookup = vfs.GetLookup(file);
                try {
                    model.Materials[i] = new(await lookup.AsFileResource<MtrlFile>(cts.Token));
                } catch (Exception) {
                    // ignore; maybe show warnings later?
                }
            }

            return model;
        }, cts.Token).Unwrap();
        _ = TryGetRenderer(out _, true);
        _rendererTask!.Task.ContinueWith(r => {
            if (r.IsCompletedSuccessfully)
                r.Result.UpdateModel(_modelTask, async texPath => {
                    var file = await vfs.FindFile(rootFolder, texPath);
                    if (file is null)
                        return null;

                    using var lookup = vfs.GetLookup(file);
                    var tex = await lookup.AsFileResource<TexFile>(cts.Token);
                    return tex.ToDdsFileFollowGameDx11Conversion();
                });
            Invalidate();
        }, cts.Token);
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

    private bool TryGetRenderer([MaybeNullWhen(false)] out MdlRenderer renderer, bool startInitializing = false) {
        if (_rendererTask?.IsCompletedSuccessfully is true) {
            renderer = _rendererTask.Result;
            return true;
        }

        renderer = null!;
        if (!startInitializing)
            return false;

        _rendererTask ??= new(Task
            .Run(() => new MdlRenderer(this))
            .ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.UiThreadInitialize();
                return r.Result;
            }, UiTaskScheduler));
        return false;
    }
}

public unsafe class MdlRenderer : DirectXRenderer<ModelViewerControl> {
    private MdlRendererShader _shader;
    private MdlRendererShader.State _shaderState;
    private Task<Model>? _modelTask;
    private Task<MdlRendererShader.ModelObject>? _modelObject;

    public MdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public MdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, true, pDevice, pDeviceContext) {
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

    public void ClearModel() {
        _modelTask = null;
        _modelObject?.ContinueWith(r => r.Result.TextureLoadStateChanged -= ResultOnTextureLoadStateChanged);
        _modelObject = null;
    }

    public void UpdateModel(Task<Model> newModelTask, Func<string, Task<DdsFile?>> ddsCallback) {
        if (_modelTask == newModelTask)
            return;

        ClearModel();
        _modelTask = newModelTask;

        newModelTask.ContinueWith(r => {
            if (_modelTask != newModelTask)
                return;

            _modelObject = Task.Run(() => {
                var modelObject = new MdlRendererShader.ModelObject(_shader, r.Result, ddsCallback);
                modelObject.TextureLoadStateChanged += ResultOnTextureLoadStateChanged;
                return modelObject;
            });
            Control.RunOnUiThreadAfter(_modelObject, _ => {
                if (_modelTask != newModelTask)
                    return;
                
                var bb = _modelTask.Result.File!.ModelBoundingBoxes;
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

    private void ResultOnTextureLoadStateChanged() {
        Control.Invalidate();
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        Span<float> colors = stackalloc float[4];
        colors[0] = 1f * Control.BackColor.R / 255;
        colors[1] = 1f * Control.BackColor.G / 255;
        colors[2] = 1f * Control.BackColor.B / 255;
        colors[3] = 1f * Control.BackColor.A / 255;

        DeviceContext->ClearRenderTargetView(pRenderTarget, ref colors[0]);

        if (_modelObject?.IsCompletedSuccessfully is true)
            _shader.Draw(_shaderState, _modelObject.Result);
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget) {
        // empty
    }

    private void ControlOnViewportChanged() {
        _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
    }
}

public sealed class CameraManager : IDisposable {
    private static readonly System3D _system3D = new(new(0, 0, -1), new(0, 1, 0));

    private readonly AbstractFileResourceViewerControl _control;

    public CameraManager(AbstractFileResourceViewerControl control) {
        _control = control;
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

    public ObjectCentricCamera ObjectCentricCamera { get; } = new(_system3D);

    public ICamera Camera => ObjectCentricCamera;

    private void MouseActivityOnPan(Point delta) {
        if (_control.MouseActivity.FirstHeldButton == MouseButtons.Left) {
            ObjectCentricCamera.YawFromTarget += delta.X * MathF.PI / 720;
            var pitch = ObjectCentricCamera.PitchFromTarget + delta.Y * MathF.PI / 720;
            ObjectCentricCamera.PitchFromTarget = Math.Clamp(pitch, MathF.PI / -2, MathF.PI / 2);
        } else if (_control.MouseActivity.FirstHeldButton == MouseButtons.Right) {
            ObjectCentricCamera.Target += _system3D.Up * delta.Y / 12f;
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

    public System3D(Vector3 forward, Vector3 up) {
        Forward = forward;
        Up = up;
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
