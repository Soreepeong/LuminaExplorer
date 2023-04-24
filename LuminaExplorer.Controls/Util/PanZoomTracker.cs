using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util.ScaleMode;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.Util;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public sealed class PanZoomTracker : IDisposable {
    public readonly MouseActivityTracker MouseActivity;

    private int _zoomExponentRange;

    private PointF _pan;
    private SizeF _size;
    private Padding _panExtraRange;
    private IScaleMode _defaultScaleMode;
    private IScaleMode? _scaleMode;

    public PanZoomTracker(MouseActivityTracker mouseActivityTracker, IScaleMode? scaleModeDefault = null) {
        _defaultScaleMode = scaleModeDefault ?? new FitInClientScaleMode(false);
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

    public Size ControlBodySize => new(ControlBodyWidth, ControlBodyHeight);

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

    public IScaleMode DefaultScaleMode {
        get => _defaultScaleMode;
        set {
            if (_defaultScaleMode == value)
                return;

            if (_scaleMode is null) {
                UpdateScaleMode(value);
                _scaleMode = null;
                _defaultScaleMode = value;
            } else {
                _defaultScaleMode = value;
                EnforceLimits();
            }
        }
    }

    public IScaleMode? ScaleMode {
        get => _scaleMode;
        set {
            if (_scaleMode == value)
                return;

            UpdateScaleMode(value);
        }
    }

    public IScaleMode EffectiveScaleMode => _scaleMode ?? _defaultScaleMode;

    public SizeF EffectiveSize => EffectiveScaleMode.CalcSize(Size, ControlBodySize, ZoomExponentUnit);

    public float EffectiveZoom => EffectiveScaleMode.CalcZoom(Size, ControlBodySize, ZoomExponentUnit);

    public float EffectiveZoomExponent => EffectiveScaleMode.CalcZoomExponent(Size, ControlBodySize, ZoomExponentUnit);

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

    public PointF DefaultScaleOrigin {
        get {
            var p = Control.PointToClient(Cursor.Position);
            return Control.ClientRectangle.Contains(p)
                ? p
                : new PointF(
                    (ControlBodyWidth + Control.Margin.Left) / 2f,
                    (ControlBodyHeight + Control.Margin.Top) / 2f);
        }
    }

    public RectangleF EffectiveRect {
        get {
            var s = EffectiveSize;
            var p = new PointF(
                (ControlBodyWidth - s.Width + Control.Margin.Left) / 2f + _pan.X,
                (ControlBodyHeight - s.Height + Control.Margin.Top) / 2f + _pan.Y);
            return new(p, s);
        }
    }

    public bool CanPan => EffectiveSize.Width > ControlBodyWidth || EffectiveSize.Height > ControlBodyHeight;

    public void Reset(SizeF? size = null) {
        var changed = false;

        var prevZoom = EffectiveZoom;
        _scaleMode = null;

        if (!_pan.IsEmpty) {
            changed = true;
            _pan = new();
        }

        if (size is not null && size.Value != _size) {
            changed = true;
            _size = size.Value;
        }

        changed |= !Equals(prevZoom, EffectiveZoom);

        if (changed)
            ViewportChanged?.Invoke();
    }

    public bool UpdateScaleMode(IScaleMode? scaleMode) => UpdateScaleMode(scaleMode, DefaultScaleOrigin);

    public bool UpdateScaleMode(IScaleMode? scaleMode, PointF cursor) {
        if (scaleMode is {} sm) {
            if (sm is FreeExponentScaleMode fzsm) {
                fzsm.ZoomExponent = Math.Clamp(fzsm.ZoomExponent, -ZoomExponentRange, +ZoomExponentRange);
                scaleMode = fzsm;
            }

            if (Equals(scaleMode.CalcZoom(Size, ControlBodySize, ZoomExponentUnit), EffectiveZoom)) {
                _scaleMode = scaleMode;
                return false;
            }
        } else if (_scaleMode is null)
            return false;

        var old = new PointF(
            (cursor.X - Control.Width / 2f - Pan.X) / EffectiveZoom,
            (cursor.Y - Control.Height / 2f - Pan.Y) / EffectiveZoom);

        _scaleMode = scaleMode;
        
        if (!UpdatePan(new(
                cursor.X - Control.Width / 2f - old.X * EffectiveZoom,
                cursor.Y - Control.Height / 2f - old.Y * EffectiveZoom)))
            ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdateZoom(float? value) => UpdateZoom(value, DefaultScaleOrigin);

    public bool UpdateZoom(float? value, PointF cursor) =>
        UpdateScaleMode(value is null
            ? null
            : new FreeScaleMode(Math.Clamp(
                value.Value,
                IScaleMode.ExponentToZoom(-ZoomExponentRange, ZoomExponentUnit),
                IScaleMode.ExponentToZoom(ZoomExponentRange, ZoomExponentUnit))), cursor);

    public bool UpdateZoomExponent(int? value) => UpdateZoom(value, DefaultScaleOrigin);

    public bool UpdateZoomExponent(int? value, PointF cursor) => 
        UpdateScaleMode(value is null
            ? null
            : new FreeExponentScaleMode(Math.Clamp(value.Value, -ZoomExponentRange, ZoomExponentRange)), cursor);

    public bool WillPanChange(PointF desiredPan, out PointF adjustedPan) {
        adjustedPan = desiredPan;
        
        var scaled = EffectiveSize;
        var xrange = MiscUtils.DivRem(scaled.Width - ControlBodyWidth, 2, out var xrem);
        var yrange = MiscUtils.DivRem(scaled.Height - ControlBodyHeight, 2, out var yrem);
        xrem = MathF.Ceiling(xrem);
        yrem = MathF.Ceiling(yrem);

        if (scaled.Width <= ControlBodyWidth)
            adjustedPan.X = 0;
        else {
            var minX = -xrange - xrem - PanExtraRange.Right;
            var maxX = xrange + PanExtraRange.Left;
            adjustedPan.X = Math.Clamp(adjustedPan.X, minX, maxX);
        }

        if (scaled.Height <= ControlBodyHeight)
            adjustedPan.Y = 0;
        else {
            var minY = -yrange - yrem - PanExtraRange.Bottom;
            var maxY = yrange + PanExtraRange.Top;
            adjustedPan.Y = Math.Clamp(adjustedPan.Y, minY, maxY);
        }

        return adjustedPan != _pan;
    }

    public bool UpdatePan(PointF desiredPan) {
        var scaled = EffectiveSize;
        var xrange = MiscUtils.DivRem(scaled.Width - ControlBodyWidth, 2, out var xrem);
        var yrange = MiscUtils.DivRem(scaled.Height - ControlBodyHeight, 2, out var yrem);
        xrem = MathF.Ceiling(xrem);
        yrem = MathF.Ceiling(yrem);

        if (scaled.Width <= ControlBodyWidth)
            desiredPan.X = 0;
        else {
            var minX = -xrange - xrem - PanExtraRange.Right;
            var maxX = xrange + PanExtraRange.Left;
            desiredPan.X = Math.Clamp(desiredPan.X, minX, maxX);
        }

        if (scaled.Height <= ControlBodyHeight)
            desiredPan.Y = 0;
        else {
            var minY = -yrange - yrem - PanExtraRange.Bottom;
            var maxY = yrange + PanExtraRange.Top;
            desiredPan.Y = Math.Clamp(desiredPan.Y, minY, maxY);
        }

        if (desiredPan == _pan)
            return false;

        _pan = desiredPan;
        ViewportChanged?.Invoke();
        return true;
    }

    public bool EnforceLimits() =>
        UpdateScaleMode(_scaleMode, DefaultScaleOrigin) ||
        UpdatePan(Pan);

    private void MouseActivityTrackerOnZoomWheel(Point origin, int delta) {
        var wheelDelta = SystemInformation.MouseWheelScrollDelta;
        var normalizedDelta =
            Math.Sign(delta) * (int) Math.Ceiling((float) Math.Abs(delta) * ZoomExponentWheelUnit / wheelDelta);

        if (normalizedDelta == 0)
            return;

        if (_scaleMode is not FreeExponentScaleMode freeScaleMode) {
            var zoomExponent = normalizedDelta switch {
                > 0 => (int) Math.Floor(EffectiveZoomExponent),
                < 0 => (int) Math.Ceiling(EffectiveZoomExponent),
                _ => throw new FailFastException("origin.Delta must be not 0 at this PointF")
            };
            UpdateZoomExponent(zoomExponent + normalizedDelta, new(origin.X, origin.Y));
        } else {
            var effectiveZoom = EffectiveZoom;
            var defaultZoom = _defaultScaleMode.CalcZoom(Size, ControlBodySize, ZoomExponentUnit);
            var nextZoom = MathF.Pow(2, 1f * (freeScaleMode.ZoomExponent + normalizedDelta) / ZoomExponentUnit);
            if (effectiveZoom < defaultZoom && defaultZoom <= nextZoom)
                UpdateScaleMode(null, new(origin.X, origin.Y));
            else if (nextZoom <= defaultZoom && defaultZoom < effectiveZoom)
                UpdateScaleMode(null, new(origin.X, origin.Y));
            else if (effectiveZoom < 1 && 1 <= nextZoom)
                UpdateScaleMode(new NoZoomScaleMode(), new(origin.X, origin.Y));
            else if (nextZoom <= 1 && 1 < effectiveZoom)
                UpdateScaleMode(new NoZoomScaleMode(), new(origin.X, origin.Y));
            else
                UpdateZoomExponent(freeScaleMode.ZoomExponent + normalizedDelta, new(origin.X, origin.Y));
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
        var fillingZoom = FitInClientScaleMode.CalcZoomStatic(Size, ControlBodySize, true);
        IScaleMode? newMode = fillingZoom switch {
            < 1 => _scaleMode is null ? new NoZoomScaleMode() : null,
            > 1 => EffectiveZoom * 2 < 1 + fillingZoom ? new FitInClientScaleMode(true) : null,
            _ => null,
        };
        UpdateScaleMode(newMode, cursor);
    }

    private void MouseActivityOnDragEnd() => UpdatePan(new((int) Pan.X, (int) Pan.Y));

    private void ControlOnResize(object? sender, EventArgs e) => EnforceLimits();

    private void ControlOnMarginChanged(object? sender, EventArgs e) => EnforceLimits();
}
