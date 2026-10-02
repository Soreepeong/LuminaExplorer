using System;
using System.Drawing;
using System.Numerics;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

public sealed class CameraManager : IDisposable {
    public readonly System3D System3D;

    private readonly AbstractFileResourceViewerControl _control;

    public CameraManager(AbstractFileResourceViewerControl control, System3D? system3D = null)
    {
        this.System3D = system3D ?? new(new(0, 0, 1), new(0, 1, 0), new(-1, 0, 0));
        this._control = control;
        this.ObjectCentricCamera = new(this.System3D);
        this._control.MouseActivity.UseLeftDrag = true;
        this._control.MouseActivity.UseInfiniteLeftDrag = true;
        this._control.MouseActivity.UseLeftDouble = true;
        this._control.MouseActivity.UseLeftDouble = true;
        this._control.MouseActivity.UseRightDrag = true;
        this._control.MouseActivity.UseInfiniteRightDrag = true;
        this._control.MouseActivity.UseMiddleDrag = true;
        this._control.MouseActivity.UseInfiniteMiddleDrag = true;
        this._control.MouseActivity.UseWheelZoom = MouseActivityTracker.WheelZoomMode.Always;
        this._control.MouseActivity.UseDoubleClickDragZoom = true;
        this._control.MouseActivity.LeftDoubleClick += this.MouseActivityOnLeftDoubleClick;
        this._control.MouseActivity.Pan += this.MouseActivityOnPan;
        this._control.MouseActivity.DoubleClickDragZoom += this.MouseActivityOnDoubleClickDragZoom;
        this._control.MouseActivity.WheelZoom += this.MouseActivityOnWheelZoom;
        this._control.ClientSizeChanged += this.ControlOnClientSizeChanged;
    }

    public void Dispose()
    {
        this._control.MouseActivity.LeftDoubleClick -= this.MouseActivityOnLeftDoubleClick;
        this._control.MouseActivity.Pan -= this.MouseActivityOnPan;
        this._control.MouseActivity.DoubleClickDragZoom -= this.MouseActivityOnDoubleClickDragZoom;
        this._control.MouseActivity.WheelZoom -= this.MouseActivityOnWheelZoom;
        this._control.ClientSizeChanged -= this.ControlOnClientSizeChanged;
    }

    public event Action? ViewportChanged;

    public ObjectCentricCamera ObjectCentricCamera { get; }

    public ICamera Camera => this.ObjectCentricCamera;

    private void MouseActivityOnPan(Point delta)
    {
        switch (this._control.MouseActivity.FirstHeldButton) {
            case MouseButtons.Left:
                this.ObjectCentricCamera.Pitch -= delta.Y * MathF.PI / 720;
                this.ObjectCentricCamera.Yaw -=
                    (this.ObjectCentricCamera.IsUpsideDown ? 1 : -1) * delta.X * MathF.PI / 720;
                break;
            case MouseButtons.Right:
                this.ObjectCentricCamera.TargetOffset -=
                    this.System3D.Right * delta.X / 120f + this.System3D.Up * delta.Y / 120f;
                break;
            case MouseButtons.Middle:
                this.ObjectCentricCamera.FovExponent += (delta.X + delta.Y) / 1200f;
                break;
        }

        this.ViewportChanged?.Invoke();
    }

    private void MouseActivityOnLeftDoubleClick(Point cursor)
    {
        this.ObjectCentricCamera.Update(
            targetOffset: Vector3.Zero,
            yaw: 0,
            pitch: 0,
            roll: 0,
            fovExponent: 0,
            resetDistance: true);
        this.ViewportChanged?.Invoke();
    }

    private void MouseActivityOnDoubleClickDragZoom(Point origin, int delta)
    {
        this.ObjectCentricCamera.Roll += delta / 120f;
        this.ViewportChanged?.Invoke();
    }

    private void MouseActivityOnWheelZoom(Point origin, int delta)
    {
        this.ObjectCentricCamera.DistanceExponent = MathF.Max(
            this.ObjectCentricCamera.DistanceExponent + delta / 12f,
            1f);
        this.ViewportChanged?.Invoke();
    }

    private void ControlOnClientSizeChanged(object? sender, EventArgs e)
    {
        this.ObjectCentricCamera.Viewport = new(this._control.ClientSize.Width, this._control.ClientSize.Height);
        this.ViewportChanged?.Invoke();
    }
}
