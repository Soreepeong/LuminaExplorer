using System.Diagnostics.CodeAnalysis;
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
    private long _clickTimerFireLeftClickAfter;
    private long _clickTimerFireRightClickAfter;
    private long _clickTimerFireMiddleClickAfter;

    public MouseActivityTracker(Control control) {
        _control = control;
        _control.MouseDown += OnMouseDown;
        _control.MouseMove += OnMouseMove;
        _control.MouseUp += OnMouseUp;
        _control.MouseLeave += OnMouseLeave;
        _control.MouseWheel += OnMouseWheel;

        _clickTimer.Elapsed += (_, _) => _control.BeginInvoke(ProcessClickTimers);
    }

    public void Dispose() {
        _control.MouseDown -= OnMouseDown;
        _control.MouseMove -= OnMouseMove;
        _control.MouseUp -= OnMouseUp;
        _control.MouseLeave -= OnMouseLeave;
        _control.MouseWheel -= OnMouseWheel;
    }

    public Control Control => _control;

    public event Action? DragStart;
    public event Action? DragEnd;

    public event PanDelegate? Pan;
    public event ZoomDelegate? ZoomDrag;
    public event ZoomDelegate? ZoomWheel;

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

    public bool IsDragging => DragBase is not null;
    public bool IsInfiniteDragging { get; private set; }
    public bool IsDraggingZoom { get; private set; }
    public bool IsDraggingPan => IsDragging && !IsDraggingZoom;

    public MouseButtons FirstHeldButton { get; private set; }
    public bool IsLeftHeld { get; private set; }
    public bool IsRightHeld { get; private set; }
    public bool IsMiddleHeld { get; private set; }
    public bool IsAnyHeld => IsLeftHeld || IsRightHeld || IsMiddleHeld;

    public bool IsLeftDoubleDown { get; private set; }
    public bool IsRightDoubleDown { get; private set; }
    public bool IsMiddleDoubleDown { get; private set; }

    public bool IsLeftDoubleUp { get; private set; }
    public bool IsRightDoubleUp { get; private set; }
    public bool IsMiddleDoubleUp { get; private set; }

    public bool UseDoubleDetection { get; set; }

    public bool UseInfiniteLeftDrag { get; set; }
    public bool UseInfiniteRightDrag { get; set; }
    public bool UseInfiniteMiddleDrag { get; set; }

    public bool UseLeftDrag {
        get => _useLeftDrag;
        set {
            _useLeftDrag = value;
            if (!value && FirstHeldButton == MouseButtons.Left)
                ExitDragState();
        }
    }

    public bool UseRightDrag {
        get => _useRightDrag;
        set {
            _useRightDrag = value;
            if (!value && FirstHeldButton == MouseButtons.Right)
                ExitDragState();
        }
    }

    public bool UseMiddleDrag {
        get => _useMiddleDrag;
        set {
            _useMiddleDrag = value;
            if (!value && FirstHeldButton == MouseButtons.Middle)
                ExitDragState();
        }
    }

    public bool UseWheelZoom { get; set; }

    public bool UseDragZoom { get; set; }
    
    public bool Enabled {
        get => _enabled;
        set {
            _enabled = value;
            if (!value)
                CancelAllOperations();
        }
    }
    
    public void CancelAllOperations() {
        ExitDragState();
        _activities.Clear();
        FirstHeldButton = MouseButtons.None;
        IsLeftHeld = IsRightHeld = IsMiddleHeld = false;
        IsLeftDoubleDown = IsRightDoubleDown = IsMiddleDoubleDown = false;
        IsLeftDoubleUp = IsRightDoubleUp = IsMiddleDoubleUp = false;
        FirstHeldButton = MouseButtons.None;
        _clickTimer.Enabled = false;
        _clickTimerFireLeftClickAfter = _clickTimerFireRightClickAfter = _clickTimerFireMiddleClickAfter = 0;
    }

    private void OnMouseDown(object? sender, MouseEventArgs e) {
        if (!_enabled)
            return;
        
        RecordActivity(new(ActivityType.Down, e.Button, e.Location));

        if (FirstHeldButton == MouseButtons.None)
            FirstHeldButton = e.Button;

        var startDrag = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                IsLeftHeld = true;
                IsLeftDoubleDown = IsDoubleDownOrUp();
                IsDraggingZoom = UseDragZoom && IsLeftDoubleDown;
                startDrag = _useLeftDrag;
                _clickTimerFireLeftClickAfter = long.MaxValue;
                break;
            }
            case MouseButtons.Right: {
                IsRightHeld = true;
                IsRightDoubleDown = IsDoubleDownOrUp();
                IsDraggingZoom = UseDragZoom && IsRightDoubleDown;
                startDrag = _useRightDrag;
                _clickTimerFireRightClickAfter = long.MaxValue;
                break;
            }
            case MouseButtons.Middle: {
                IsMiddleHeld = true;
                IsMiddleDoubleDown = IsDoubleDownOrUp();
                IsDraggingZoom = UseDragZoom && IsMiddleDoubleDown;
                startDrag = _useMiddleDrag;
                _clickTimerFireMiddleClickAfter = long.MaxValue;
                break;
            }
        }

        ProcessClickTimers();

        if (startDrag && DragOrigin is null) {
            DragOrigin = e.Location;
            _control.Capture = true;

            if (!UseDoubleDetection)
                EnterDragState(DragOrigin.Value);
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e) {
        if (!_enabled)
            return;

        if (DragOrigin is not { } dragOrigin)
            return;

        Point delta;
        _control.Capture = true;
        if (DragBase is { } dragBase) {
            var pos = e.Location;
            delta = new(pos.X - dragBase.X, pos.Y - dragBase.Y);
            if (IsInfiniteDragging)
                Cursor.Position = _control.PointToScreen(dragBase);
            else
                DragBase = pos;
        } else if ((_useLeftDrag && IsLeftHeld) ||
                   (_useRightDrag && IsRightHeld) ||
                   (_useMiddleDrag && IsMiddleHeld)) {
            var doubleClickRect = new Rectangle(dragOrigin, SystemInformation.DoubleClickSize);
            doubleClickRect.X -= doubleClickRect.Width / 2;
            doubleClickRect.Y -= doubleClickRect.Height / 2;
            delta = new(e.Location.X - dragOrigin.X, e.Location.Y - dragOrigin.Y);
            if (!UseDoubleDetection || !doubleClickRect.Contains(e.Location))
                EnterDragState(e.Location);
        } else
            return;

        if (IsDragging && !delta.IsEmpty) {
            var controlAbs = _control.PointToScreen(new());

            if (UseDragZoom && (
                    IsMiddleHeld ||
                    FirstHeldButton switch {
                        MouseButtons.Left => IsLeftDoubleDown,
                        MouseButtons.Right => IsRightDoubleDown,
                        _ => false,
                    })) {
                var dn = delta.X + delta.Y;
                if (dn != 0)
                    ZoomDrag?.Invoke(dragOrigin, dn);
            } else {
                Pan?.Invoke(delta);
            }

            if (!IsInfiniteDragging) {
                var controlAbsNew = _control.PointToScreen(new());

                DragBase = new(
                    DragBase!.Value.X + controlAbs.X - controlAbsNew.X,
                    DragBase!.Value.Y + controlAbs.Y - controlAbsNew.Y);
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e) {
        if (!_enabled)
            return;

        RecordActivity(new(ActivityType.Up, e.Button, e.Location));

        IsLeftDoubleUp = IsRightDoubleUp = IsMiddleDoubleUp = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                IsLeftHeld = false;
                if (!_activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                IsLeftDoubleUp = IsDoubleDownOrUp();

                var blockDouble = false;
                LeftImmediateClick?.Invoke(e.Location, ref blockDouble);
                if (!UseDoubleDetection)
                    LeftClick?.Invoke(e.Location);
                if (blockDouble)
                    IsLeftDoubleUp = false;

                if (IsLeftDoubleUp) {
                    _activities.Clear();
                    LeftDoubleClick?.Invoke(e.Location);
                } else if (!blockDouble && UseDoubleDetection && !IsDragging)
                    _clickTimerFireLeftClickAfter = Environment.TickCount64 + SystemInformation.DoubleClickTime;

                break;
            }
            case MouseButtons.Right: {
                IsRightHeld = false;
                if (!_activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                IsRightDoubleUp = IsDoubleDownOrUp();

                var blockDouble = false;
                RightImmediateClick?.Invoke(e.Location, ref blockDouble);
                if (!UseDoubleDetection)
                    RightClick?.Invoke(e.Location);
                if (blockDouble)
                    IsRightDoubleUp = false;

                if (IsRightDoubleUp) {
                    _activities.Clear();
                    RightDoubleClick?.Invoke(e.Location);
                } else if (!blockDouble && UseDoubleDetection && !IsDragging)
                    _clickTimerFireRightClickAfter = Environment.TickCount64 + SystemInformation.DoubleClickTime;

                break;
            }
            case MouseButtons.Middle: {
                IsMiddleHeld = false;
                if (!_activities[^1].IsInDoubleClickRange(e.Location))
                    break;

                IsMiddleDoubleUp = IsDoubleDownOrUp();

                var blockDouble = false;
                MiddleImmediateClick?.Invoke(e.Location, ref blockDouble);
                if (!UseDoubleDetection)
                    MiddleClick?.Invoke(e.Location);
                if (blockDouble)
                    IsMiddleDoubleUp = false;

                if (IsMiddleDoubleUp) {
                    _activities.Clear();
                    MiddleDoubleClick?.Invoke(e.Location);
                } else if (!blockDouble && UseDoubleDetection && !IsDragging)
                    _clickTimerFireMiddleClickAfter = Environment.TickCount64 + SystemInformation.DoubleClickTime;

                break;
            }
        }

        ProcessClickTimers();

        if (FirstHeldButton switch {
                MouseButtons.Left => !IsLeftHeld,
                MouseButtons.Right => !IsRightHeld,
                MouseButtons.Middle => !IsMiddleHeld,
                _ => false,
            }) {
            ExitDragState();
        }

        if (!IsAnyHeld)
            FirstHeldButton = MouseButtons.None;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e) {
        if (!_enabled)
            return;

        if (UseWheelZoom && e.Delta != 0)
            ZoomWheel?.Invoke(e.Location, e.Delta);
    }

    private void OnMouseLeave(object? sender, EventArgs e) {
        if (!_enabled)
            return;

        ExitDragState();
    }

    private void EnterDragState(Point dragBase) {
        if (DragBase is not null)
            return;

        DragBase = dragBase;
        RecordActivity(new(ActivityType.DragStart, MouseButtons.None, DragBase.Value));

        if ((UseInfiniteLeftDrag && FirstHeldButton == MouseButtons.Left) ||
            (UseInfiniteRightDrag && FirstHeldButton == MouseButtons.Right) ||
            (UseInfiniteMiddleDrag && FirstHeldButton == MouseButtons.Middle)) {
            IsInfiniteDragging = true;
            Cursor.Position = _control.PointToScreen(dragBase);
            Cursor.Hide();
        }
        
        DragStart?.Invoke();
    }

    private void ExitDragState() {
        if (DragOrigin is null)
            return;

        if (DragBase is { } dragBase) {
            RecordActivity(new(ActivityType.DragEnd, MouseButtons.None, dragBase));
            if (IsInfiniteDragging) {
                Cursor.Position = _control.PointToScreen(dragBase);
                Cursor.Show();
                IsInfiniteDragging = false;
            }
        }

        IsDraggingZoom = false;

        _control.Capture = false;

        DragOrigin = DragBase = null;
        
        DragEnd?.Invoke();
    }

    private void RecordActivity(Activity activity) {
        if (_activities.Count >= 8)
            _activities.RemoveRange(0, _activities.Count - 8 + 1);
        _activities.Add(activity);
    }

    private bool IsDoubleDownOrUp() =>
        UseDoubleDetection &&
        _activities.Count >= 3 &&
        _activities[^1].Button == _activities[^3].Button &&
        _activities[^1].Button == _activities[^2].Button &&
        _activities[^1].Type == _activities[^3].Type &&
        _activities[^2].Type is not ActivityType.DragEnd and not ActivityType.DragStart &&
        _activities[^1].Tick - _activities[^3].Tick <= SystemInformation.DoubleClickTime &&
        _activities[^1].IsInDoubleClickRange(_activities[^3].Point);

    private void ProcessClickTimers() {
        var now = Environment.TickCount64;
        if (_clickTimerFireLeftClickAfter <= now) {
            LeftClick?.Invoke(
                _activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Left && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? _control.PointToClient(Cursor.Position));
            _clickTimerFireLeftClickAfter = long.MaxValue;
        }

        if (_clickTimerFireRightClickAfter <= now) {
            RightClick?.Invoke(
                _activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Right && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? _control.PointToClient(Cursor.Position));
            _clickTimerFireRightClickAfter = long.MaxValue;
        }

        if (_clickTimerFireMiddleClickAfter <= now) {
            MiddleClick?.Invoke(
                _activities
                    .Select(x => (Activity?) x)
                    .LastOrDefault(x => x!.Value.Button == MouseButtons.Middle && x.Value.Type == ActivityType.Up)
                    ?.Point
                ?? _control.PointToClient(Cursor.Position));
            _clickTimerFireMiddleClickAfter = long.MaxValue;
        }

        var next = _clickTimerFireLeftClickAfter;
        next = Math.Min(next, _clickTimerFireRightClickAfter);
        next = Math.Min(next, _clickTimerFireMiddleClickAfter);
        if (next == long.MaxValue) {
            _clickTimer.Enabled = false;
        } else {
            _clickTimer.Enabled = true;
            _clickTimer.Interval = (int) (next - now);
        }
    }

    public readonly struct Activity {
        public readonly long Tick = Environment.TickCount64;
        public readonly ActivityType Type;
        public readonly MouseButtons Button;
        public readonly Point Point;

        public Activity(ActivityType type, MouseButtons button, Point point) {
            Type = type;
            Button = button;
            Point = point;
        }

        public bool IsInDoubleClickRange(Point point) {
            var doubleClickRect = new Rectangle(point, SystemInformation.DoubleClickSize);
            doubleClickRect.X -= doubleClickRect.Width / 2;
            doubleClickRect.Y -= doubleClickRect.Height / 2;
            return doubleClickRect.Contains(Point);
        }
    }

    public enum ActivityType {
        Down,
        DragStart,
        Up,
        DragEnd,
    }

    public delegate void PanDelegate(Point delta);

    public delegate void ZoomDelegate(Point origin, int delta);

    public delegate void ClickDelegate(Point cursor);

    public delegate bool BarrieredClickDelegate(Point cursor, ref bool blockBecomingDoubleClick);
}
