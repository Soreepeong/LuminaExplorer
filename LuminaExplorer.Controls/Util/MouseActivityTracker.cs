namespace LuminaExplorer.Controls.Util;

public class MouseActivityTracker {
    private readonly Control _control;
    private readonly List<Activity> _activities = new();

    private bool _useLeftDrag;
    private bool _useRightDrag;
    private bool _useMiddleDrag;
    private Point _dragDelta;

    public MouseActivityTracker(Control control) {
        _control = control;
    }

    public Point? DragOrigin { get; private set; }
    public Point? DragBase { get; private set; }

    public bool IsDragging => DragBase is not null;

    public bool IsLeftHeld { get; private set; }
    public bool IsRightHeld { get; private set; }
    public bool IsMiddleHeld { get; private set; }

    public bool IsLeftDoubleDown { get; private set; }
    public bool IsRightDoubleDown { get; private set; }
    public bool IsMiddleDoubleDown { get; private set; }

    public bool IsLeftDoubleUp { get; private set; }
    public bool IsRightDoubleUp { get; private set; }
    public bool IsMiddleDoubleUp { get; private set; }

    public bool UseDoubleDetection { get; set; }

    public bool UseLeftDrag {
        get => _useLeftDrag;
        set => _useLeftDrag = value;
    }

    public bool UseRightDrag {
        get => _useRightDrag;
        set => _useRightDrag = value;
    }

    public bool UseMiddleDrag {
        get => _useMiddleDrag;
        set => _useMiddleDrag = value;
    }

    public bool TryGetNextDragDelta(out Point dragDelta) {
        if (!IsDragging || _dragDelta.IsEmpty)
            dragDelta = new();
        else
            dragDelta = _dragDelta;
        _dragDelta = new();
        return !dragDelta.IsEmpty;
    }

    public void FeedMouseDown(MouseEventArgs e) {
        RecordActivity(new(ActivityType.Down, e.Button, Cursor.Position));

        var startDrag = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                IsLeftHeld = true;
                IsLeftDoubleDown = IsDoubleDownOrUp();
                startDrag = _useLeftDrag;
                break;
            }
            case MouseButtons.Right: {
                IsRightHeld = true;
                IsRightDoubleDown = IsDoubleDownOrUp();
                startDrag = _useRightDrag;
                break;
            }
            case MouseButtons.Middle: {
                IsMiddleHeld = true;
                IsMiddleDoubleDown = IsDoubleDownOrUp();
                startDrag = _useMiddleDrag;
                break;
            }
        }

        if (startDrag && DragOrigin is null) {
            DragOrigin = Cursor.Position;
            _dragDelta = new();
            _control.Capture = true;

            if (!UseDoubleDetection)
                EnterDragState(DragOrigin.Value);
        }
    }

    public void FeedMouseMove(MouseEventArgs e) {
        if (DragOrigin is not { } dragOrigin)
            return;

        _control.Capture = true;
        if (DragBase is { } dragBase) {
            var pos = Cursor.Position;
            _dragDelta.X += pos.X - dragBase.X;
            _dragDelta.Y += pos.Y - dragBase.Y;
            Cursor.Position = dragBase;
        } else if ((_useLeftDrag && IsLeftHeld) ||
                   (_useRightDrag && IsRightHeld) ||
                   (_useMiddleDrag && IsMiddleHeld)) {
            var pos = Cursor.Position;
            var doubleClickRect = new Rectangle(dragOrigin, SystemInformation.DoubleClickSize);
            doubleClickRect.X -= doubleClickRect.Width / 2;
            doubleClickRect.Y -= doubleClickRect.Height / 2;
            _dragDelta = new(pos.X - dragOrigin.X, pos.Y - dragOrigin.Y);
            if (!UseDoubleDetection || !doubleClickRect.Contains(pos))
                EnterDragState(pos);
        }
    }

    public void FeedMouseUp(MouseEventArgs e) {
        RecordActivity(new(ActivityType.Up, e.Button, Cursor.Position));

        IsLeftDoubleUp = IsRightDoubleUp = IsMiddleDoubleUp = false;
        switch (e.Button) {
            case MouseButtons.Left: {
                IsLeftHeld = false;
                IsLeftDoubleUp = IsDoubleDownOrUp();
                if (IsLeftDoubleUp)
                    _activities.Clear();
                break;
            }
            case MouseButtons.Right: {
                IsRightHeld = false;
                IsRightDoubleUp = IsDoubleDownOrUp();
                if (IsRightDoubleUp)
                    _activities.Clear();
                break;
            }
            case MouseButtons.Middle: {
                IsMiddleHeld = false;
                IsMiddleDoubleUp = IsDoubleDownOrUp();
                if (IsMiddleDoubleUp)
                    _activities.Clear();
                break;
            }
        }

        if (!(_useLeftDrag && IsLeftHeld) &&
            !(_useRightDrag && IsRightHeld) &&
            !(_useMiddleDrag && IsMiddleHeld)) {
            ExitDragState();
        }
    }

    public void FeedMouseLeave() {
        ExitDragState();
    }

    private void EnterDragState(Point dragBase) {
        if (DragBase is not null)
            return;
        
        DragBase = dragBase;
        RecordActivity(new(ActivityType.DragStart, MouseButtons.None, DragBase.Value));

        Cursor.Position = DragBase.Value;
        Cursor.Hide();
    }

    private void ExitDragState() {
        if (DragOrigin is not { } dragOrigin)
            return;
        
        if (IsDragging) {
            RecordActivity(new(ActivityType.DragEnd, MouseButtons.None, dragOrigin));
            Cursor.Position = dragOrigin;
            Cursor.Show();
        }

        _control.Capture = false;

        DragOrigin = DragBase = null;
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
        _activities[^1].IsInDoubleClickRange(_activities[^3]);

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

        public bool IsInDoubleClickRange(Activity older) {
            var doubleClickRect = new Rectangle(older.Point, SystemInformation.DoubleClickSize);
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
}
