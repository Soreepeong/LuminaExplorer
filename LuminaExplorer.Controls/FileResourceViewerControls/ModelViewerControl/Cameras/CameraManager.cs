using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

public sealed class CameraManager : IDisposable {
    public readonly System3D System3D;

    private readonly AbstractFileResourceViewerControl _control;

    public CameraManager(AbstractFileResourceViewerControl control, System3D? system3D = null) {
        System3D = system3D ?? new(new(0, 0, 1), new(0, 1, 0), new(-1, 0, 0));
        _control = control;
        ObjectCentricCamera = new(System3D);
        _control.MouseActivity.UseLeftDrag = true;
        _control.MouseActivity.UseInfiniteLeftDrag = true;
        _control.MouseActivity.UseLeftDouble = true;
        _control.MouseActivity.UseLeftDouble = true;
        _control.MouseActivity.UseRightDrag = true;
        _control.MouseActivity.UseInfiniteRightDrag = true;
        _control.MouseActivity.UseMiddleDrag = true;
        _control.MouseActivity.UseInfiniteMiddleDrag = true;
        _control.MouseActivity.UseWheelZoom = MouseActivityTracker.WheelZoomMode.Always;
        _control.MouseActivity.UseDoubleClickDragZoom = true;
        _control.MouseActivity.LeftDoubleClick += MouseActivityOnLeftDoubleClick;
        _control.MouseActivity.Pan += MouseActivityOnPan;
        _control.MouseActivity.DoubleClickDragZoom += MouseActivityOnDoubleClickDragZoom;
        _control.MouseActivity.WheelZoom += MouseActivityOnWheelZoom;
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
        switch (_control.MouseActivity.FirstHeldButton) {
            case MouseButtons.Left:
                ObjectCentricCamera.Pitch -= delta.Y * MathF.PI / 720;
                ObjectCentricCamera.Yaw -=
                    (ObjectCentricCamera.IsUpsideDown ? 1 : -1) * delta.X * MathF.PI / 720;
                break;
            case MouseButtons.Right:
                ObjectCentricCamera.TargetOffset -= System3D.Right * delta.X / 120f + System3D.Up * delta.Y / 120f;
                break;
            case MouseButtons.Middle:
                ObjectCentricCamera.FovExponent += (delta.X + delta.Y) / 1200f;
                break;
        }

        ViewportChanged?.Invoke();
    }

    private void MouseActivityOnLeftDoubleClick(Point cursor) {
        ObjectCentricCamera.Update(
            targetOffset: Vector3.Zero,
            yaw: 0,
            pitch: 0,
            roll: 0,
            fovExponent: 0,
            resetDistance: true);
        ViewportChanged?.Invoke();
    }

    private void MouseActivityOnDoubleClickDragZoom(Point origin, int delta) {
        ObjectCentricCamera.Roll += delta / 120f;
        ViewportChanged?.Invoke();
    }

    private void MouseActivityOnWheelZoom(Point origin, int delta) {
        ObjectCentricCamera.DistanceExponent = MathF.Max(ObjectCentricCamera.DistanceExponent + delta / 12f, 1f);
        ViewportChanged?.Invoke();
    }

    private void ControlOnClientSizeChanged(object? sender, EventArgs e) {
        ObjectCentricCamera.Viewport = new(_control.ClientSize.Width, _control.ClientSize.Height);
        ViewportChanged?.Invoke();
    }
}
