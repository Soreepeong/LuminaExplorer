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
    private float _lastKnownZoom;
    private float _rotation;

    public PanZoomTracker(MouseActivityTracker mouseActivityTracker, IScaleMode? scaleModeDefault = null) {
        _defaultScaleMode = scaleModeDefault ?? new FitInClientScaleMode(false);
        MouseActivity = mouseActivityTracker;
        MouseActivity.Pan += MouseActivityTrackerOnPan;
        MouseActivity.ZoomDrag += MouseActivityTrackerOnZoomDrag;
        MouseActivity.ZoomWheel += MouseActivityTrackerOnZoomWheel;
        MouseActivity.LeftDoubleClick += MouseActivityOnLeftDoubleClick;
        MouseActivity.DragEnd += MouseActivityOnDragEnd;
        Control.ClientSizeChanged += ControlOnClientSizeChanged;

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
        Control.ClientSizeChanged -= ControlOnClientSizeChanged;
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

    public SizeF EffectiveRotatedSize => EffectiveZoom * RotatedSize;

    public SizeF RotatedSize {
        get {
            var (sin, cos) = MathF.SinCos(_rotation);
            
            var p1 = new PointF(-Size.Height * sin, Size.Height * cos);
            var p2 = new PointF(Size.Width * cos, Size.Width * sin);
            var p3 = new PointF(Size.Width * cos - Size.Height * sin, Size.Width * sin + Size.Height * cos);
            return new(
                Math.Max(p1.X, Math.Max(p2.X, p3.X)) - Math.Min(p1.X, Math.Min(p2.X, p3.X)),
                Math.Max(p1.Y, Math.Max(p2.Y, p3.Y)) - Math.Min(p1.Y, Math.Min(p2.Y, p3.Y)));
        }
    }

    public SizeF EffectiveSize => EffectiveScaleMode.CalcSize(Size, ControlBodySize, ZoomExponentUnit);

    public float EffectiveZoom => EffectiveScaleMode.CalcZoom(Size, ControlBodySize, ZoomExponentUnit);

    public float EffectiveZoomExponent => EffectiveScaleMode.CalcZoomExponent(Size, ControlBodySize, ZoomExponentUnit);

    public float PanSpeedMultiplier { get; set; } = 2f;

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

    public bool IsPanClampingActive => !MouseActivity.IsRightHeld;

    public SizeF Size {
        get => _size;
        set {
            _size = value;
            EnforceLimits();
        }
    }

    public float Rotation {
        get => _rotation;
        set => UpdateRotation(value);
    }

    public PointF DefaultOrigin {
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
            var s = EffectiveRotatedSize;
            var p = new PointF(
                (ControlBodyWidth - s.Width + Control.Margin.Left) / 2f + _pan.X,
                (ControlBodyHeight - s.Height + Control.Margin.Top) / 2f + _pan.Y);
            return new(p, s);
        }
    }

    public bool CanPan => EffectiveRotatedSize.Width > ControlBodyWidth || EffectiveRotatedSize.Height > ControlBodyHeight;

    public void Reset(SizeF? size, float? rotation) {
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

        if (rotation is not null && !Equals(rotation.Value, _rotation)) {
            changed = true;
            _rotation = rotation.Value;
        }

        changed |= !Equals(prevZoom, EffectiveZoom);

        if (!EnforceLimits() && changed)
            ViewportChanged?.Invoke();
    }

    public bool UpdateScaleMode(IScaleMode? scaleMode) => UpdateScaleMode(scaleMode, DefaultOrigin);

    public bool UpdateScaleMode(IScaleMode? scaleMode, PointF cursor) {
        if (scaleMode is { } sm) {
            if (sm is FreeExponentScaleMode fzsm) {
                fzsm.ZoomExponent = Math.Clamp(fzsm.ZoomExponent, -ZoomExponentRange, +ZoomExponentRange);
                scaleMode = fzsm;
            }

            if (Equals(scaleMode.CalcZoom(RotatedSize, ControlBodySize, ZoomExponentUnit), EffectiveZoom)) {
                _scaleMode = scaleMode;
                return false;
            }
        } else if (_scaleMode is null) {
            if (Equals(_lastKnownZoom, EffectiveZoom))
                return false;

            ViewportChanged?.Invoke();
            return true;
        }

        var old = new PointF(
            (cursor.X - Control.Width / 2f - Pan.X) / EffectiveZoom,
            (cursor.Y - Control.Height / 2f - Pan.Y) / EffectiveZoom);

        _scaleMode = scaleMode;

        _lastKnownZoom = EffectiveZoom;
        if (!UpdatePan(new(
                cursor.X - Control.Width / 2f - old.X * _lastKnownZoom,
                cursor.Y - Control.Height / 2f - old.Y * _lastKnownZoom)))
            ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdateZoom(float? value) => UpdateZoom(value, DefaultOrigin);

    public bool UpdateZoom(float? value, PointF cursor) =>
        UpdateScaleMode(value is null
            ? null
            : new FreeScaleMode(Math.Clamp(
                value.Value,
                IScaleMode.ExponentToZoom(-ZoomExponentRange, ZoomExponentUnit),
                IScaleMode.ExponentToZoom(ZoomExponentRange, ZoomExponentUnit))), cursor);

    public bool UpdateZoomExponent(int? value) => UpdateZoom(value, DefaultOrigin);

    public bool UpdateZoomExponent(int? value, PointF cursor) =>
        UpdateScaleMode(value is null
            ? null
            : new FreeExponentScaleMode(Math.Clamp(value.Value, -ZoomExponentRange, ZoomExponentRange)), cursor);

    public bool WillPanChange(PointF desiredPan, out PointF adjustedPan) {
        adjustedPan = desiredPan;

        if (IsPanClampingActive) {
            var scaled = EffectiveRotatedSize;
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
        }

        return adjustedPan != _pan;
    }

    public bool UpdatePan(PointF desiredPan) {
        if (IsPanClampingActive) {
            var scaled = EffectiveRotatedSize;
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
        }

        if (desiredPan == _pan)
            return false;

        _pan = desiredPan;
        ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdateRotation(float rotation) => UpdateRotation(rotation, DefaultOrigin);

    public bool UpdateRotation(float rotation, PointF origin) {
        _rotation %= 360;
        if (Equals(rotation, _rotation))
            return false;

        var (s, c) = MathF.SinCos(-_rotation);

        // translate point back to origin:
        var unrotatedPan = new PointF(
            Pan.X + ControlBodyWidth / 2f - origin.X,
            Pan.Y + ControlBodyHeight / 2f - origin.Y);
        unrotatedPan = new(
            unrotatedPan.X * c - unrotatedPan.Y * s,
            unrotatedPan.X * s + unrotatedPan.Y * c);

        _rotation = rotation;
        
        (s, c) = MathF.SinCos(rotation);
        if (!UpdatePan(new(
            origin.X - ControlBodyWidth / 2f + unrotatedPan.X * c - unrotatedPan.Y * s,
            origin.Y - ControlBodyHeight / 2f + unrotatedPan.X * s + unrotatedPan.Y * c)))
            ViewportChanged?.Invoke();
        return true;
    }

    public bool EnforceLimits() =>
        UpdateScaleMode(_scaleMode, DefaultOrigin) ||
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
            var defaultZoom = _defaultScaleMode.CalcZoom(RotatedSize, ControlBodySize, ZoomExponentUnit);
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
        if (MouseActivity.FirstHeldButton == MouseButtons.Left)
            UpdatePan(new(Pan.X + delta.X * PanSpeedMultiplier, Pan.Y + delta.Y * PanSpeedMultiplier));
        else
            UpdateRotation(
                (_rotation * 180 / MathF.PI + (delta.X + delta.Y)) * MathF.PI / 180,
                MouseActivity.DragOrigin ?? DefaultOrigin);
    }

    private void MouseActivityOnLeftDoubleClick(Point cursor) {
        var fillingZoom = FitInClientScaleMode.CalcZoomStatic(RotatedSize, ControlBodySize, true);
        IScaleMode? newMode = fillingZoom switch {
            < 1 => _scaleMode is null ? new NoZoomScaleMode() : null,
            > 1 => EffectiveZoom * 2 < 1 + fillingZoom ? new FitInClientScaleMode(true) : null,
            _ => null,
        };
        UpdateScaleMode(newMode, cursor);
    }

    private void MouseActivityOnDragEnd() {
        UpdatePan(new((int) Pan.X, (int) Pan.Y));
        UpdateRotation(
            MathF.Round(_rotation * 180 / MathF.PI / 15) * 15 * MathF.PI / 180,
            MouseActivity.DragOrigin ?? DefaultOrigin);
    }

    private void ControlOnClientSizeChanged(object? sender, EventArgs e) {
        if (!EnforceLimits())
            ViewportChanged?.Invoke();
    }
}
