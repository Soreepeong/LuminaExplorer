using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.Util;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public sealed class PanZoomTracker : IDisposable {
    public readonly MouseActivityTracker MouseActivity;

    private int _zoomExponentRange;
    private int? _zoomExponent;

    private PointF _pan;
    private SizeF _size;
    private Padding _panExtraRange;

    public PanZoomTracker(MouseActivityTracker mouseActivityTracker) {
        MouseActivity = mouseActivityTracker;
        MouseActivity.Pan += MouseActivityTrackerOnPan;
        MouseActivity.ZoomDrag += MouseActivityTrackerOnZoomDrag;
        MouseActivity.ZoomWheel += MouseActivityTrackerOnZoomWheel;
        MouseActivity.LeftDoubleClick += MouseActivityOnLeftDoubleClick;
        MouseActivity.DragEnd += MouseActivityOnDragEnd;
        Control.Resize += ControlOnResize;
        Control.MarginChanged += ControlOnMarginChanged;

        ZoomExponentUnit = Math.Max(1, (1 << 8) * SystemInformation.MouseWheelScrollDelta);
        ZoomExponentRange = Math.Max(1, ZoomExponentUnit << 3);
        ZoomExponentWheelUnit = Math.Max(1, ZoomExponentUnit >> 3);
        ZoomExponentDragUnit = Math.Max(1, ZoomExponentUnit >> 8);
    }

    public void Dispose() {
        MouseActivity.Pan -= MouseActivityTrackerOnPan;
        MouseActivity.ZoomDrag -= MouseActivityTrackerOnZoomDrag;
        MouseActivity.ZoomWheel -= MouseActivityTrackerOnZoomWheel;
        MouseActivity.LeftDoubleClick -= MouseActivityOnLeftDoubleClick;
        MouseActivity.DragEnd -= MouseActivityOnDragEnd;
        Control.Resize -= ControlOnResize;
        Control.MarginChanged -= ControlOnMarginChanged;
    }

    public event Action? ViewportChanged;

    public Control Control => MouseActivity.Control;

    public int ControlBodyWidth => Control.ClientSize.Width - Control.Margin.Horizontal;

    public int ControlBodyHeight => Control.ClientSize.Height - Control.Margin.Vertical;

    public int ZoomExponentUnit { get; set; }

    public int ZoomExponentWheelUnit { get; set; }

    public int ZoomExponentDragUnit { get; set; }

    public int ZoomExponentRange {
        get => _zoomExponentRange;
        set {
            _zoomExponentRange = value;
            EnforceLimits();
        }
    }

    public int? ZoomExponent => _zoomExponent;

    public float EffectiveZoomExponent => MathF.Log2(EffectiveZoom) * ZoomExponentUnit;

    public float? Zoom => _zoomExponent is null ? null : EffectiveZoom;

    public float EffectiveZoom {
        get {
            if (_zoomExponent is { } zoomExponent)
                return MathF.Pow(2, 1f * zoomExponent / ZoomExponentUnit);
            return DefaultZoom;
        }
    }

    public float DefaultZoom => Size.IsEmpty
        ? 1f
        : Size.Width <= ControlBodyWidth &&
          Size.Height < ControlBodyHeight - Control.Margin.Vertical
            ? 1f
            : FillingZoom;

    public float FillingZoom => Size.IsEmpty
        ? 1f
        : ControlBodyWidth * Size.Height > Size.Width * ControlBodyHeight
            ? 1f * ControlBodyHeight / Size.Height
            : 1f * ControlBodyWidth / Size.Width;

    public Padding PanExtraRange {
        get => _panExtraRange;
        set {
            if (_panExtraRange == value)
                return;
            _panExtraRange = value;
            EnforceLimits();
        }
    }

    public PointF Pan {
        get => _pan;
        set => UpdatePan(value);
    }

    public SizeF Size {
        get => _size;
        set {
            _size = value;
            UpdatePan(Pan);
        }
    }

    public SizeF EffectiveSize => new(
        (int) Math.Round(Size.Width * EffectiveZoom),
        (int) Math.Round(Size.Height * EffectiveZoom));

    public RectangleF EffectiveRect {
        get {
            var s = EffectiveSize;
            var p = new PointF(
                (ControlBodyWidth - s.Width + Control.Margin.Left) / 2f + _pan.X,
                (ControlBodyHeight - s.Height + Control.Margin.Top) / 2f + _pan.Y);
            return new(p, s);
        }
    }

    public void Reset(SizeF? size = null) {
        var changed = false;
        if (_zoomExponent is not null) {
            changed = !Equals(EffectiveZoom, DefaultZoom);
            _zoomExponent = null;
        }

        if (!_pan.IsEmpty) {
            changed = true;
            _pan = new();
        }

        if (size is not null && size.Value != _size) {
            changed = true;
            _size = size.Value;
        }

        if (changed)
            ViewportChanged?.Invoke();
    }

    public bool UpdateZoomExponent(int? value, PointF cursor) {
        if (value is not null)
            value = Math.Clamp(value.Value, -ZoomExponentRange, +ZoomExponentRange);
        if (value == _zoomExponent)
            return false;

        var old = new PointF(
            (cursor.X - Control.Width / 2f - Pan.X) / EffectiveZoom,
            (cursor.Y - Control.Height / 2f - Pan.Y) / EffectiveZoom);

        _zoomExponent = value;
        if (!UpdatePan(new(
                cursor.X - Control.Width / 2f - old.X * EffectiveZoom,
                cursor.Y - Control.Height / 2f - old.Y * EffectiveZoom)))
            ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdatePan(PointF value) {
        var scaled = EffectiveSize;
        var xrange = MiscUtils.DivRem(scaled.Width - ControlBodyWidth, 2, out var xrem);
        var yrange = MiscUtils.DivRem(scaled.Height - ControlBodyHeight, 2, out var yrem);
        xrem = MathF.Ceiling(xrem);
        yrem = MathF.Ceiling(yrem);

        if (scaled.Width <= ControlBodyWidth)
            value.X = 0;
        else {
            var minX = -xrange - xrem - PanExtraRange.Right;
            var maxX = xrange + PanExtraRange.Left;
            value.X = Math.Clamp(value.X, minX, maxX);
        }

        if (scaled.Height <= ControlBodyHeight)
            value.Y = 0;
        else {
            var minY = -yrange - yrem - PanExtraRange.Bottom;
            var maxY = yrange + PanExtraRange.Top;
            value.Y = Math.Clamp(value.Y, minY, maxY);
        }

        if (value == _pan)
            return false;

        _pan = value;
        ViewportChanged?.Invoke();
        return true;
    }

    public bool EnforceLimits() =>
        UpdateZoomExponent(_zoomExponent, new(
            (ControlBodyWidth + Control.Margin.Left) / 2f,
            (ControlBodyHeight + Control.Margin.Top) / 2f)) ||
        UpdatePan(Pan);

    private void MouseActivityTrackerOnZoomWheel(Point origin, int delta) {
        var wheelDelta = SystemInformation.MouseWheelScrollDelta;
        var normalizedDelta =
            Math.Sign(delta) * (int) Math.Ceiling((float) Math.Abs(delta) * ZoomExponentWheelUnit / wheelDelta);

        if (normalizedDelta == 0)
            return;

        if (_zoomExponent is not { } zoomExponent) {
            zoomExponent = normalizedDelta switch {
                > 0 => (int) Math.Floor(EffectiveZoomExponent),
                < 0 => (int) Math.Ceiling(EffectiveZoomExponent),
                _ => throw new FailFastException("origin.Delta must be not 0 at this PointF")
            };
            UpdateZoomExponent(zoomExponent + normalizedDelta, new(origin.X, origin.Y));
        } else {
            var effectiveZoom = EffectiveZoom;
            var defaultZoom = DefaultZoom;
            var nextZoom = MathF.Pow(2, 1f * (zoomExponent + normalizedDelta) / ZoomExponentUnit);
            if (effectiveZoom < defaultZoom && defaultZoom <= nextZoom)
                UpdateZoomExponent(null, new(origin.X, origin.Y));
            else if (nextZoom <= defaultZoom && defaultZoom < effectiveZoom)
                UpdateZoomExponent(null, new(origin.X, origin.Y));
            else if (effectiveZoom < 1 && 1 <= nextZoom)
                UpdateZoomExponent(0, new(origin.X, origin.Y));
            else if (nextZoom <= 1 && 1 < effectiveZoom)
                UpdateZoomExponent(0, new(origin.X, origin.Y));
            else
                UpdateZoomExponent(zoomExponent + normalizedDelta, new(origin.X, origin.Y));
        }
    }

    private void MouseActivityTrackerOnZoomDrag(Point origin, int delta) {
        var multiplier = 1 << (
            (MouseActivity.IsLeftHeld ? 1 : 0) +
            (MouseActivity.IsRightHeld ? 1 : 0) +
            (MouseActivity.IsMiddleHeld ? 1 : 0) - 1);
        UpdateZoomExponent((int) Math.Round(EffectiveZoomExponent) + delta * ZoomExponentDragUnit * multiplier, origin);
    }

    private void MouseActivityTrackerOnPan(Point delta) {
        var multiplier = 1 << (
            (MouseActivity.IsLeftHeld ? 1 : 0) +
            (MouseActivity.IsRightHeld ? 1 : 0) +
            (MouseActivity.IsMiddleHeld ? 1 : 0) - 1);
        UpdatePan(new(
            Pan.X + delta.X * multiplier,
            Pan.Y + delta.Y * multiplier));
    }

    private void MouseActivityOnLeftDoubleClick(Point cursor) {
        float? zoom = FillingZoom switch {
            < 1 => Zoom is null ? 1 : null,
            > 1 => EffectiveZoom * 2 < 1 + FillingZoom ? FillingZoom : null,
            _ => null,
        };
        UpdateZoomExponent(zoom is null ? null : (int) Math.Round(MathF.Log2(zoom.Value) * ZoomExponentUnit), cursor);
    }

    private void MouseActivityOnDragEnd() => UpdatePan(new((int) Pan.X, (int) Pan.Y));

    private void ControlOnResize(object? sender, EventArgs e) => EnforceLimits();

    private void ControlOnMarginChanged(object? sender, EventArgs e) => EnforceLimits();
}
