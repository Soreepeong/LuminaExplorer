using System;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

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