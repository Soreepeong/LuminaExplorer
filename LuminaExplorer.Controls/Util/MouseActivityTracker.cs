using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Timer = System.Timers.Timer;

namespace LuminaExplorer.Controls.Util;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class MouseActivityTracker : IDisposable {
    private readonly Control _control;
    private readonly List<Activity> _activities = new();

    private bool _enabled = true;

    private bool _useLeftDrag;
    private bool _useRightDrag;
    private bool _useMiddleDrag;

    private readonly Timer _clickTimer = new();
    private long _clickTimerFireLeftClickAfter = long.MaxValue;
    private long _clickTimerFireRightClickAfter = long.MaxValue;
    private long _clickTimerFireMiddleClickAfter = long.MaxValue;

    public MouseActivityTracker(Control control)
    {
        this._control = control;
        this._control.MouseDown += this.OnMouseDown;
        this._control.MouseMove += this.OnMouseMove;
        this._control.MouseUp += this.OnMouseUp;
        this._control.MouseLeave += this.OnMouseLeave;
        this._control.MouseWheel += this.OnMouseWheel;

        this._clickTimer.Elapsed += (_, _) => this._control.BeginInvoke(this.ProcessClickTimers);
    }

    public void Dispose()
    {
        this._control.MouseDown -= this.OnMouseDown;
        this._control.MouseMove -= this.OnMouseMove;
        this._control.MouseUp -= this.OnMouseUp;
        this._control.MouseLeave -= this.OnMouseLeave;
        this._control.MouseWheel -= this.OnMouseWheel;
        this._clickTimer.Dispose();
    }

    public Control Control => this._control;

    public event Action? DragStart;
    public event Action? DragEnd;

    public event PanDelegate? Pan;
    public event ZoomDelegate? DoubleClickDragZoom;
    public event ZoomDelegate? WheelZoom;

    public event BarrieredClickDelegate? LeftImmediateClick;
    public event BarrieredClickDelegate? RightImmediateClick;
    public event BarrieredClickDelegate? MiddleImmediateClick;

    public event ClickDelegate? LeftClick;
    public event ClickDelegate? RightClick;
    public event ClickDelegate? MiddleClick;

    public event ClickDelegate? LeftDoubleClick;
    public event ClickDelegate? RightDoubleClick;
    public event ClickDelegate? MiddleDoubleClick;

    public Point? DragOrigin { get; private set; }
    public Point? DragBase { get; private set; }

    public bool IsDragging => this.DragBase is not null;
    public bool IsInfiniteDragging { get; private set; }
    public bool IsDraggingZoom { get; private set; }
    public bool IsDraggingPan => this.IsDragging && !this.IsDraggingZoom;

    public MouseButtons FirstHeldButton { get; private set; }
    public bool IsLeftHeld { get; private set; }
    public bool IsRightHeld { get; private set; }
    public bool IsMiddleHeld { get; private set; }
    public bool IsAnyHeld => this.IsLeftHeld || this.IsRightHeld || this.IsMiddleHeld;

    public bool IsLeftDoubleDown { get; private set; }
    public bool IsRightDoubleDown { get; private set; }
    public bool IsMiddleDoubleDown { get; private set; }

    public bool IsLeftDoubleUp { get; private set; }
    public bool IsRightDoubleUp { get; private set; }
    public bool IsMiddleDoubleUp { get; private set; }

    public bool UseLeftDouble { get; set; }
    public bool UseRightDouble { get; set; }
    public bool UseMiddleDouble { get; set; }

    public bool UseInfiniteLeftDrag { get; set; }
    public bool UseInfiniteRightDrag { get; set; }
    public bool UseInfiniteMiddleDrag { get; set; }

    public bool UseLeftDrag {
        get => this._useLeftDrag;
        set {
            this._useLeftDrag = value;
            if (!value && this.FirstHeldButton == MouseButtons.Left) this.ExitDragState();
        }
    }

    public bool UseRightDrag {
        get => this._useRightDrag;
        set {
            this._useRightDrag = value;
            if (!value && this.FirstHeldButton == MouseButtons.Right) this.ExitDragState();
        }
    }

    public bool UseMiddleDrag {
        get => this._useMiddleDrag;
        set {
            this._useMiddleDrag = value;
            if (!value && this.FirstHeldButton == MouseButtons.Middle) this.ExitDragState();
        }
    }

    public WheelZoomMode UseWheelZoom { get; set; }

    public bool UseDoubleClickDragZoom { get; set; }

    public bool Enabled {
        get => this._enabled;
        set {
            this._enabled = value;
            if (!value) this.CancelAllOperations();
        }
    }

    public void CancelAllOperations()
    {
        this.ExitDragState();
        this._activities.Clear();
        this.FirstHeldButton = MouseButtons.None;
        this.IsLeftHeld = this.IsRightHeld = this.IsMiddleHeld = false;
        this.IsLeftDoubleDown = this.IsRightDoubleDown = this.IsMiddleDoubleDown = false;
        this.IsLeftDoubleUp = this.IsRightDoubleUp = this.IsMiddleDoubleUp = false;
        this.FirstHeldButton = MouseButtons.None;
        this._clickTimer.Enabled = false;
        this._clickTimerFireLeftClickAfter = long.MaxValue;
        this._clickTimerFireRightClickAfter = long.MaxValue;
        this._clickTimerFireMiddleClickAfter = long.MaxValue;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (!this._enabled)
            return;

        this.RecordActivity(new(ActivityType.Down, e.Button, e.Location));

        if (this.FirstHeldButton == MouseButtons.None) this.FirstHeldButton = e.Button;

        var startDrag = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                this.IsLeftHeld = true;
                this.IsLeftDoubleDown = this.IsDoubleDownOrUp();
                this.IsDraggingZoom = this.UseDoubleClickDragZoom && this.IsLeftDoubleDown;
                startDrag = this._useLeftDrag;
                this._clickTimerFireLeftClickAfter = long.MaxValue;
                break;
            }
            case MouseButtons.Right: {
                this.IsRightHeld = true;
                this.IsRightDoubleDown = this.IsDoubleDownOrUp();
                this.IsDraggingZoom = this.UseDoubleClickDragZoom && this.IsRightDoubleDown;
                startDrag = this._useRightDrag;
                this._clickTimerFireRightClickAfter = long.MaxValue;
                break;
            }
            case MouseButtons.Middle: {
                this.IsMiddleHeld = true;
                this.IsMiddleDoubleDown = this.IsDoubleDownOrUp();
                this.IsDraggingZoom = this.UseDoubleClickDragZoom && this.IsMiddleDoubleDown;
                startDrag = this._useMiddleDrag;
                this._clickTimerFireMiddleClickAfter = long.MaxValue;
                break;
            }
        }

        this.ProcessClickTimers();

        if (startDrag && this.DragOrigin is null) {
            this.DragOrigin = e.Location;
            this._control.Capture = true;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!this._enabled)
            return;

        if (this.DragOrigin is not { } dragOrigin)
            return;

        Point delta;
        this._control.Capture = true;
        if (this.DragBase is { } dragBase) {
            var pos = e.Location;
            delta = new(pos.X - dragBase.X, pos.Y - dragBase.Y);
            if (this.IsInfiniteDragging)
                Cursor.Position = this._control.PointToScreen(dragBase);
            else
                this.DragBase = pos;
        } else if ((this._useLeftDrag && this.IsLeftHeld) ||
                   (this._useRightDrag && this.IsRightHeld) ||
                   (this._useMiddleDrag && this.IsMiddleHeld)) {
            var doubleClickRect = new Rectangle(dragOrigin, SystemInformation.DoubleClickSize);
            doubleClickRect.X -= doubleClickRect.Width / 2;
            doubleClickRect.Y -= doubleClickRect.Height / 2;
            delta = new(e.Location.X - dragOrigin.X, e.Location.Y - dragOrigin.Y);
            if ((this.FirstHeldButton == MouseButtons.Left && !this.UseLeftDouble) ||
                (this.FirstHeldButton == MouseButtons.Right && !this.UseRightDouble) ||
                (this.FirstHeldButton == MouseButtons.Middle && !this.UseMiddleDouble) ||
                !doubleClickRect.Contains(e.Location))
                this.EnterDragState(e.Location);
        } else
            return;

        if (this.IsDragging && !delta.IsEmpty) {
            var controlAbs = this._control.PointToScreen(new());

            if (this.UseDoubleClickDragZoom && (this.FirstHeldButton switch {
                    MouseButtons.Left => this.IsLeftDoubleDown,
                    MouseButtons.Middle => this.IsMiddleDoubleDown,
                    MouseButtons.Right => this.IsRightDoubleDown,
                    _ => false,
                })) {
                var dn = delta.X + delta.Y;
                if (dn != 0) this.DoubleClickDragZoom?.Invoke(dragOrigin, dn);
            } else {
                this.Pan?.Invoke(delta);
            }

            if (!this.IsInfiniteDragging) {
                var controlAbsNew = this._control.PointToScreen(new());

                this.DragBase = new(
                    this.DragBase!.Value.X + controlAbs.X - controlAbsNew.X,
                    this.DragBase!.Value.Y + controlAbs.Y - controlAbsNew.Y);
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (!this._enabled)
            return;

        this.RecordActivity(new(ActivityType.Up, e.Button, e.Location));

        this.IsLeftDoubleUp = this.IsRightDoubleUp = this.IsMiddleDoubleUp = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                this.IsLeftHeld = false;
                if (!this._activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                var eligibleForClick = this.FirstHeldButton == MouseButtons.Left && !this.IsDragging;
                if (!eligibleForClick) {
                    this._activities.Clear();
                    this.IsLeftDoubleUp = false;
                } else {
                    this.IsLeftDoubleUp = this.IsDoubleDownOrUp();

                    var blockDouble = false;
                    this.LeftImmediateClick?.Invoke(e.Location, ref blockDouble);
                    if (!this.UseLeftDouble) this.LeftClick?.Invoke(e.Location);
                    if (blockDouble) this.IsLeftDoubleUp = false;

                    if (this.IsLeftDoubleUp) {
                        this._activities.Clear();
                        this.LeftDoubleClick?.Invoke(e.Location);
                    } else if (!blockDouble && this.UseLeftDouble && !this.IsDragging)
                        this._clickTimerFireLeftClickAfter =
                            Environment.TickCount64 + SystemInformation.DoubleClickTime;
                }

                break;
            }
            case MouseButtons.Right: {
                this.IsRightHeld = false;
                if (!this._activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                var eligibleForClick = this.FirstHeldButton == MouseButtons.Right && !this.IsDragging;
                if (!eligibleForClick) {
                    this._activities.Clear();
                    this.IsRightDoubleUp = false;
                } else {
                    this.IsRightDoubleUp = this.IsDoubleDownOrUp();

                    var blockDouble = false;
                    this.RightImmediateClick?.Invoke(e.Location, ref blockDouble);
                    if (!this.UseRightDouble) this.RightClick?.Invoke(e.Location);
                    if (blockDouble) this.IsRightDoubleUp = false;

                    if (this.IsRightDoubleUp) {
                        this._activities.Clear();
                        this.RightDoubleClick?.Invoke(e.Location);
                    } else if (!blockDouble && this.UseRightDouble && !this.IsDragging)
                        this._clickTimerFireRightClickAfter =
                            Environment.TickCount64 + SystemInformation.DoubleClickTime;
                }

                break;
            }
            case MouseButtons.Middle: {
                this.IsMiddleHeld = false;
                if (!this._activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                var eligibleForClick = this.FirstHeldButton == MouseButtons.Middle && !this.IsDragging;
                if (!eligibleForClick) {
                    this._activities.Clear();
                    this.IsMiddleDoubleUp = false;
                } else {
                    this.IsMiddleDoubleUp = this.IsDoubleDownOrUp();

                    var blockDouble = false;
                    this.MiddleImmediateClick?.Invoke(e.Location, ref blockDouble);
                    if (!this.UseMiddleDouble) this.MiddleClick?.Invoke(e.Location);
                    if (blockDouble) this.IsMiddleDoubleUp = false;

                    if (this.IsMiddleDoubleUp) {
                        this._activities.Clear();
                        this.MiddleDoubleClick?.Invoke(e.Location);
                    } else if (!blockDouble && this.UseMiddleDouble && !this.IsDragging)
                        this._clickTimerFireMiddleClickAfter =
                            Environment.TickCount64 + SystemInformation.DoubleClickTime;
                }

                break;
            }
        }

        this.ProcessClickTimers();

        if (this.FirstHeldButton switch {
                MouseButtons.Left => !this.IsLeftHeld,
                MouseButtons.Right => !this.IsRightHeld,
                MouseButtons.Middle => !this.IsMiddleHeld,
                _ => false,
            }) {
            this.ExitDragState();
        }

        if (!this.IsAnyHeld) this.FirstHeldButton = MouseButtons.None;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (!this._enabled)
            return;

        if (e.Delta != 0 && (this.UseWheelZoom is WheelZoomMode.Always ||
                (this.UseWheelZoom is WheelZoomMode.RequireControlKey && Control.ModifierKeys.HasFlag(Keys.Control))))
            this.WheelZoom?.Invoke(e.Location, e.Delta);
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (!this._enabled)
            return;

        this.ExitDragState();
    }

    private void EnterDragState(Point dragBase)
    {
        if (this.DragBase is not null)
            return;

        this.DragBase = dragBase;
        this.RecordActivity(new(ActivityType.DragStart, MouseButtons.None, this.DragBase.Value));

        if ((this.UseInfiniteLeftDrag && this.FirstHeldButton == MouseButtons.Left) ||
            (this.UseInfiniteRightDrag && this.FirstHeldButton == MouseButtons.Right) ||
            (this.UseInfiniteMiddleDrag && this.FirstHeldButton == MouseButtons.Middle)) {
            this.IsInfiniteDragging = true;
            Cursor.Position = this._control.PointToScreen(dragBase);
            Cursor.Hide();
        }

        this.DragStart?.Invoke();
    }

    private void ExitDragState()
    {
        if (this.DragOrigin is null)
            return;

        if (this.DragBase is { } dragBase) {
            this.RecordActivity(new(ActivityType.DragEnd, MouseButtons.None, dragBase));
            if (this.IsInfiniteDragging) {
                Cursor.Position = this._control.PointToScreen(dragBase);
                Cursor.Show();
                this.IsInfiniteDragging = false;
            }
        }

        this.IsDraggingZoom = false;

        this._control.Capture = false;

        this.DragOrigin = this.DragBase = null;

        this.DragEnd?.Invoke();
    }

    private void RecordActivity(Activity activity)
    {
        if (this._activities.Count >= 8) this._activities.RemoveRange(0, this._activities.Count - 8 + 1);
        this._activities.Add(activity);
    }

    private bool IsDoubleDownOrUp() =>
        this._activities.Count >= 3 &&
        ((this._activities[^1].Button == MouseButtons.Left && this.UseLeftDouble) ||
            (this._activities[^1].Button == MouseButtons.Right && this.UseRightDouble) ||
            (this._activities[^1].Button == MouseButtons.Middle && this.UseMiddleDouble)) &&
        this._activities[^1].Button == this._activities[^3].Button &&
        this._activities[^1].Button == this._activities[^2].Button &&
        this._activities[^1].Type == this._activities[^3].Type &&
        this._activities[^2].Type is not ActivityType.DragEnd and not ActivityType.DragStart &&
        this._activities[^1].Tick - this._activities[^3].Tick <= SystemInformation.DoubleClickTime &&
        this._activities[^1].IsInDoubleClickRange(this._activities[^3].Point);

    private void ProcessClickTimers()
    {
        var now = Environment.TickCount64;
        if (this._clickTimerFireLeftClickAfter <= now) {
            this.LeftClick?.Invoke(
                this._activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Left && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? this._control.PointToClient(Cursor.Position));
            this._clickTimerFireLeftClickAfter = long.MaxValue;
        }

        if (this._clickTimerFireRightClickAfter <= now) {
            this.RightClick?.Invoke(
                this._activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Right && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? this._control.PointToClient(Cursor.Position));
            this._clickTimerFireRightClickAfter = long.MaxValue;
        }

        if (this._clickTimerFireMiddleClickAfter <= now) {
            this.MiddleClick?.Invoke(
                this._activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Middle && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? this._control.PointToClient(Cursor.Position));
            this._clickTimerFireMiddleClickAfter = long.MaxValue;
        }

        var next = this._clickTimerFireLeftClickAfter;
        next = Math.Min(next, this._clickTimerFireRightClickAfter);
        next = Math.Min(next, this._clickTimerFireMiddleClickAfter);
        if (next == long.MaxValue) {
            this._clickTimer.Enabled = false;
        } else {
            this._clickTimer.Enabled = true;
            this._clickTimer.Interval = (int) (next - now);
        }
    }

    public readonly struct Activity {
        public readonly long Tick = Environment.TickCount64;
        public readonly ActivityType Type;
        public readonly MouseButtons Button;
        public readonly Point Point;

        public Activity(ActivityType type, MouseButtons button, Point point)
        {
            this.Type = type;
            this.Button = button;
            this.Point = point;
        }

        public bool IsInDoubleClickRange(Point point)
        {
            var doubleClickRect = new Rectangle(point, SystemInformation.DoubleClickSize);
            doubleClickRect.X -= doubleClickRect.Width / 2;
            doubleClickRect.Y -= doubleClickRect.Height / 2;
            return doubleClickRect.Contains(this.Point);
        }
    }

    public enum ActivityType {
        Down,
        DragStart,
        Up,
        DragEnd,
    }

    public enum WheelZoomMode {
        Disabled,
        Always,
        RequireControlKey,
    }

    public delegate void PanDelegate(Point delta);

    public delegate void ZoomDelegate(Point origin, int delta);

    public delegate void ClickDelegate(Point cursor);

    public delegate bool BarrieredClickDelegate(Point cursor, ref bool blockBecomingDoubleClick);
}
