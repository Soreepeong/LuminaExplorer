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

    public PanZoomTracker(MouseActivityTracker mouseActivityTracker, IScaleMode? scaleModeDefault = null)
    {
        this._defaultScaleMode = scaleModeDefault ?? new FitInClientScaleMode(false);
        this.MouseActivity = mouseActivityTracker;
        this.MouseActivity.Pan += this.MouseActivityTrackerOnPan;
        this.MouseActivity.DoubleClickDragZoom += this.MouseActivityTrackerOnDoubleClickDragZoom;
        this.MouseActivity.WheelZoom += this.MouseActivityTrackerOnWheelZoom;
        this.MouseActivity.LeftDoubleClick += this.MouseActivityOnLeftDoubleClick;
        this.MouseActivity.DragEnd += this.MouseActivityOnDragEnd;
        this.Control.ClientSizeChanged += this.ControlOnClientSizeChanged;

        this.ZoomExponentUnit = Math.Max(1, (1 << 8) * SystemInformation.MouseWheelScrollDelta);
        this.ZoomExponentRange = Math.Max(1, this.ZoomExponentUnit << 3);
        this.ZoomExponentWheelUnit = Math.Max(1, this.ZoomExponentUnit >> 3);
        this.ZoomExponentDragUnit = Math.Max(1, this.ZoomExponentUnit >> 8);
    }

    public void Dispose()
    {
        this.MouseActivity.Pan -= this.MouseActivityTrackerOnPan;
        this.MouseActivity.DoubleClickDragZoom -= this.MouseActivityTrackerOnDoubleClickDragZoom;
        this.MouseActivity.WheelZoom -= this.MouseActivityTrackerOnWheelZoom;
        this.MouseActivity.LeftDoubleClick -= this.MouseActivityOnLeftDoubleClick;
        this.MouseActivity.DragEnd -= this.MouseActivityOnDragEnd;
        this.Control.ClientSizeChanged -= this.ControlOnClientSizeChanged;
    }

    public event Action? ViewportChanged;

    public Control Control => this.MouseActivity.Control;

    public int ControlBodyWidth => this.Control.ClientSize.Width - this.Control.Margin.Horizontal;

    public int ControlBodyHeight => this.Control.ClientSize.Height - this.Control.Margin.Vertical;

    public Size ControlBodySize => new(this.ControlBodyWidth, this.ControlBodyHeight);

    public int ZoomExponentUnit { get; set; }

    public int ZoomExponentWheelUnit { get; set; }

    public int ZoomExponentDragUnit { get; set; }

    public int ZoomExponentRange {
        get => this._zoomExponentRange;
        set {
            this._zoomExponentRange = value;
            this.EnforceLimits();
        }
    }

    public IScaleMode DefaultScaleMode {
        get => this._defaultScaleMode;
        set {
            if (this._defaultScaleMode == value)
                return;

            if (this._scaleMode is null) {
                this.UpdateScaleMode(value);
                this._scaleMode = null;
                this._defaultScaleMode = value;
            } else {
                this._defaultScaleMode = value;
                this.EnforceLimits();
            }
        }
    }

    public IScaleMode? ScaleMode {
        get => this._scaleMode;
        set {
            if (this._scaleMode == value)
                return;

            this.UpdateScaleMode(value);
        }
    }

    public IScaleMode EffectiveScaleMode => this._scaleMode ?? this._defaultScaleMode;

    public SizeF EffectiveRotatedSize => this.EffectiveZoom * this.RotatedSize;

    public SizeF RotatedSize {
        get {
            var (sin, cos) = MathF.SinCos(this._rotation);

            var p1 = new PointF(-this.Size.Height * sin, this.Size.Height * cos);
            var p2 = new PointF(this.Size.Width * cos, this.Size.Width * sin);
            var p3 = new PointF(
                this.Size.Width * cos - this.Size.Height * sin,
                this.Size.Width * sin + this.Size.Height * cos);
            return new(
                Math.Max(p1.X, Math.Max(p2.X, p3.X)) - Math.Min(p1.X, Math.Min(p2.X, p3.X)),
                Math.Max(p1.Y, Math.Max(p2.Y, p3.Y)) - Math.Min(p1.Y, Math.Min(p2.Y, p3.Y)));
        }
    }

    public SizeF EffectiveSize =>
        this.EffectiveScaleMode.CalcSize(this.Size, this.ControlBodySize, this.ZoomExponentUnit);

    public float EffectiveZoom =>
        this.EffectiveScaleMode.CalcZoom(this.Size, this.ControlBodySize, this.ZoomExponentUnit);

    public float EffectiveZoomExponent => this.EffectiveScaleMode.CalcZoomExponent(
        this.Size,
        this.ControlBodySize,
        this.ZoomExponentUnit);

    public float PanSpeedMultiplier { get; set; } = 2f;

    public Padding PanExtraRange {
        get => this._panExtraRange;
        set {
            if (this._panExtraRange == value)
                return;
            this._panExtraRange = value;
            this.EnforceLimits();
        }
    }

    public PointF Pan {
        get => this._pan;
        set => this.UpdatePan(value);
    }

    public bool IsPanClampingActive => !this.MouseActivity.IsRightHeld;

    public SizeF Size {
        get => this._size;
        set {
            this._size = value;
            this.EnforceLimits();
        }
    }

    public float Rotation {
        get => this._rotation;
        set => this.UpdateRotation(value);
    }

    public PointF DefaultOrigin {
        get {
            var p = this.Control.PointToClient(Cursor.Position);
            return this.Control.ClientRectangle.Contains(p)
                ? p
                : new PointF(
                    (this.ControlBodyWidth + this.Control.Margin.Left) / 2f,
                    (this.ControlBodyHeight + this.Control.Margin.Top) / 2f);
        }
    }

    public RectangleF EffectiveRect {
        get {
            var s = this.EffectiveRotatedSize;
            var p = new PointF(
                (this.ControlBodyWidth - s.Width + this.Control.Margin.Left) / 2f + this._pan.X,
                (this.ControlBodyHeight - s.Height + this.Control.Margin.Top) / 2f + this._pan.Y);
            return new(p, s);
        }
    }

    public bool CanPan => this.EffectiveRotatedSize.Width > this.ControlBodyWidth ||
        this.EffectiveRotatedSize.Height > this.ControlBodyHeight;

    public void Reset(SizeF? size, float? rotation)
    {
        var changed = false;

        var prevZoom = this.EffectiveZoom;
        this._scaleMode = null;

        if (!this._pan.IsEmpty) {
            changed = true;
            this._pan = new();
        }

        if (size is not null && size.Value != this._size) {
            changed = true;
            this._size = size.Value;
        }

        if (rotation is not null && !Equals(rotation.Value, this._rotation)) {
            changed = true;
            this._rotation = rotation.Value;
        }

        changed |= !Equals(prevZoom, this.EffectiveZoom);

        if (!this.EnforceLimits() && changed) this.ViewportChanged?.Invoke();
    }

    public bool UpdateScaleMode(IScaleMode? scaleMode) => this.UpdateScaleMode(scaleMode, this.DefaultOrigin);

    public bool UpdateScaleMode(IScaleMode? scaleMode, PointF cursor)
    {
        if (scaleMode is { } sm) {
            if (sm is FreeExponentScaleMode fzsm) {
                fzsm.ZoomExponent = Math.Clamp(fzsm.ZoomExponent, -this.ZoomExponentRange, +this.ZoomExponentRange);
                scaleMode = fzsm;
            }

            if (Equals(
                    scaleMode.CalcZoom(this.RotatedSize, this.ControlBodySize, this.ZoomExponentUnit),
                    this.EffectiveZoom)) {
                this._scaleMode = scaleMode;
                return false;
            }
        } else if (this._scaleMode is null) {
            if (Equals(this._lastKnownZoom, this.EffectiveZoom))
                return false;

            this.ViewportChanged?.Invoke();
            return true;
        }

        var old = new PointF(
            (cursor.X - this.Control.Width / 2f - this.Pan.X) / this.EffectiveZoom,
            (cursor.Y - this.Control.Height / 2f - this.Pan.Y) / this.EffectiveZoom);

        this._scaleMode = scaleMode;

        this._lastKnownZoom = this.EffectiveZoom;
        if (!this.UpdatePan(
                new(
                    cursor.X - this.Control.Width / 2f - old.X * this._lastKnownZoom,
                    cursor.Y - this.Control.Height / 2f - old.Y * this._lastKnownZoom)))
            this.ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdateZoom(float? value) => this.UpdateZoom(value, this.DefaultOrigin);

    public bool UpdateZoom(float? value, PointF cursor) =>
        this.UpdateScaleMode(
            value is null
                ? null
                : new FreeScaleMode(
                    Math.Clamp(
                        value.Value,
                        IScaleMode.ExponentToZoom(-this.ZoomExponentRange, this.ZoomExponentUnit),
                        IScaleMode.ExponentToZoom(this.ZoomExponentRange, this.ZoomExponentUnit))),
            cursor);

    public bool UpdateZoomExponent(int? value) => this.UpdateZoom(value, this.DefaultOrigin);

    public bool UpdateZoomExponent(int? value, PointF cursor) =>
        this.UpdateScaleMode(
            value is null
                ? null
                : new FreeExponentScaleMode(Math.Clamp(value.Value, -this.ZoomExponentRange, this.ZoomExponentRange)),
            cursor);

    public bool WillPanChange(PointF desiredPan, out PointF adjustedPan)
    {
        adjustedPan = desiredPan;

        if (this.IsPanClampingActive) {
            var scaled = this.EffectiveRotatedSize;
            var xrange = MiscUtils.DivRem(scaled.Width - this.ControlBodyWidth, 2, out var xrem);
            var yrange = MiscUtils.DivRem(scaled.Height - this.ControlBodyHeight, 2, out var yrem);
            xrem = MathF.Ceiling(xrem);
            yrem = MathF.Ceiling(yrem);

            if (scaled.Width <= this.ControlBodyWidth)
                adjustedPan.X = 0;
            else {
                var minX = -xrange - xrem - this.PanExtraRange.Right;
                var maxX = xrange + this.PanExtraRange.Left;
                adjustedPan.X = Math.Clamp(adjustedPan.X, minX, maxX);
            }

            if (scaled.Height <= this.ControlBodyHeight)
                adjustedPan.Y = 0;
            else {
                var minY = -yrange - yrem - this.PanExtraRange.Bottom;
                var maxY = yrange + this.PanExtraRange.Top;
                adjustedPan.Y = Math.Clamp(adjustedPan.Y, minY, maxY);
            }
        }

        return adjustedPan != this._pan;
    }

    public bool UpdatePan(PointF desiredPan)
    {
        if (this.IsPanClampingActive) {
            var scaled = this.EffectiveRotatedSize;
            var xrange = MiscUtils.DivRem(scaled.Width - this.ControlBodyWidth, 2, out var xrem);
            var yrange = MiscUtils.DivRem(scaled.Height - this.ControlBodyHeight, 2, out var yrem);
            xrem = MathF.Ceiling(xrem);
            yrem = MathF.Ceiling(yrem);

            if (scaled.Width <= this.ControlBodyWidth)
                desiredPan.X = 0;
            else {
                var minX = -xrange - xrem - this.PanExtraRange.Right;
                var maxX = xrange + this.PanExtraRange.Left;
                desiredPan.X = Math.Clamp(desiredPan.X, minX, maxX);
            }

            if (scaled.Height <= this.ControlBodyHeight)
                desiredPan.Y = 0;
            else {
                var minY = -yrange - yrem - this.PanExtraRange.Bottom;
                var maxY = yrange + this.PanExtraRange.Top;
                desiredPan.Y = Math.Clamp(desiredPan.Y, minY, maxY);
            }
        }

        if (desiredPan == this._pan)
            return false;

        this._pan = desiredPan;
        this.ViewportChanged?.Invoke();
        return true;
    }

    public bool UpdateRotation(float rotation) => this.UpdateRotation(rotation, this.DefaultOrigin);

    public bool UpdateRotation(float rotation, PointF origin)
    {
        this._rotation %= 360;
        if (Equals(rotation, this._rotation))
            return false;

        var (s, c) = MathF.SinCos(-this._rotation);

        // translate point back to origin:
        var unrotatedPan = new PointF(
            this.Pan.X + this.ControlBodyWidth / 2f - origin.X,
            this.Pan.Y + this.ControlBodyHeight / 2f - origin.Y);
        unrotatedPan = new(
            unrotatedPan.X * c - unrotatedPan.Y * s,
            unrotatedPan.X * s + unrotatedPan.Y * c);

        this._rotation = rotation;

        (s, c) = MathF.SinCos(rotation);
        if (!this.UpdatePan(
                new(
                    origin.X - this.ControlBodyWidth / 2f + unrotatedPan.X * c - unrotatedPan.Y * s,
                    origin.Y - this.ControlBodyHeight / 2f + unrotatedPan.X * s + unrotatedPan.Y * c)))
            this.ViewportChanged?.Invoke();
        return true;
    }

    public bool EnforceLimits() =>
        this.UpdateScaleMode(this._scaleMode, this.DefaultOrigin) || this.UpdatePan(this.Pan);

    private void MouseActivityTrackerOnWheelZoom(Point origin, int delta)
    {
        var wheelDelta = SystemInformation.MouseWheelScrollDelta;
        var normalizedDelta =
            Math.Sign(delta) * (int) Math.Ceiling((float) Math.Abs(delta) * this.ZoomExponentWheelUnit / wheelDelta);

        if (normalizedDelta == 0)
            return;

        if (this._scaleMode is not FreeExponentScaleMode freeScaleMode) {
            var zoomExponent = normalizedDelta switch {
                > 0 => (int) Math.Floor(this.EffectiveZoomExponent),
                < 0 => (int) Math.Ceiling(this.EffectiveZoomExponent),
                _ => throw new FailFastException("origin.Delta must be not 0 at this PointF"),
            };
            this.UpdateZoomExponent(zoomExponent + normalizedDelta, new(origin.X, origin.Y));
        } else {
            var effectiveZoom = this.EffectiveZoom;
            var defaultZoom = this._defaultScaleMode.CalcZoom(
                this.RotatedSize,
                this.ControlBodySize,
                this.ZoomExponentUnit);
            var nextZoom = MathF.Pow(2, 1f * (freeScaleMode.ZoomExponent + normalizedDelta) / this.ZoomExponentUnit);
            if (effectiveZoom < defaultZoom && defaultZoom <= nextZoom)
                this.UpdateScaleMode(null, new(origin.X, origin.Y));
            else if (nextZoom <= defaultZoom && defaultZoom < effectiveZoom)
                this.UpdateScaleMode(null, new(origin.X, origin.Y));
            else if (effectiveZoom < 1 && 1 <= nextZoom)
                this.UpdateScaleMode(new NoZoomScaleMode(), new(origin.X, origin.Y));
            else if (nextZoom <= 1 && 1 < effectiveZoom)
                this.UpdateScaleMode(new NoZoomScaleMode(), new(origin.X, origin.Y));
            else
                this.UpdateZoomExponent(freeScaleMode.ZoomExponent + normalizedDelta, new(origin.X, origin.Y));
        }
    }

    private void MouseActivityTrackerOnDoubleClickDragZoom(Point origin, int delta)
    {
        var multiplier = 1 << (
            (this.MouseActivity.IsLeftHeld ? 1 : 0) +
            (this.MouseActivity.IsRightHeld ? 1 : 0) +
            (this.MouseActivity.IsMiddleHeld ? 1 : 0) - 1);
        this.UpdateZoomExponent(
            (int) Math.Round(this.EffectiveZoomExponent) + delta * this.ZoomExponentDragUnit * multiplier,
            origin);
    }

    private void MouseActivityTrackerOnPan(Point delta)
    {
        switch (this.MouseActivity.FirstHeldButton) {
            case MouseButtons.Left:
                this.UpdatePan(
                    new(
                        this.Pan.X + delta.X * this.PanSpeedMultiplier,
                        this.Pan.Y + delta.Y * this.PanSpeedMultiplier));
                break;
            case MouseButtons.Right:
                this.UpdateRotation(
                    (this._rotation * 180 / MathF.PI + (delta.X + delta.Y)) * MathF.PI / 180,
                    this.MouseActivity.DragOrigin ?? this.DefaultOrigin);
                break;
            case MouseButtons.Middle:
                this.MouseActivityTrackerOnDoubleClickDragZoom(
                    Point.Truncate(this.MouseActivity.DragOrigin ?? this.DefaultOrigin),
                    delta.X + delta.Y);
                break;
        }
    }

    private void MouseActivityOnLeftDoubleClick(Point cursor)
    {
        var fillingZoom = FitInClientScaleMode.CalcZoomStatic(this.RotatedSize, this.ControlBodySize, true);
        IScaleMode? newMode = fillingZoom switch {
            < 1 => this._scaleMode is null ? new NoZoomScaleMode() : null,
            > 1 => this.EffectiveZoom * 2 < 1 + fillingZoom ? new FitInClientScaleMode(true) : null,
            _ => null,
        };
        this.UpdateScaleMode(newMode, cursor);
    }

    private void MouseActivityOnDragEnd()
    {
        this.UpdatePan(new((int) this.Pan.X, (int) this.Pan.Y));
        this.UpdateRotation(
            MathF.Round(this._rotation * 180 / MathF.PI / 15) * 15 * MathF.PI / 180,
            this.MouseActivity.DragOrigin ?? this.DefaultOrigin);
    }

    private void ControlOnClientSizeChanged(object? sender, EventArgs e)
    {
        if (!this.EnforceLimits()) this.ViewportChanged?.Invoke();
    }
}
