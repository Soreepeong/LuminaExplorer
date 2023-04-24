using System;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util.ScaleMode;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl {
    private readonly TimedKeyState[] _keys = new TimedKeyState[256];

    protected override bool IsInputKey(Keys keyData) {
        switch (keyData & Keys.KeyCode) {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
                return true;
        }

        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);

        var key = RedirectKey(e.KeyCode);
        var keyb = (byte) key;
        if (MouseActivity.Enabled) {
            switch (key) {
                case Keys.Up when e.Alt: // Disable rotation
                    Rotation = 0;
                    break;
                case Keys.Right when e.Alt: // Rotate 90 degrees clockwise
                    Rotation = MathF.PI / 2;
                    break;
                case Keys.Down when e.Alt: // Rotate 180 degrees
                    Rotation = MathF.PI;
                    break;
                case Keys.Left when e.Alt: // Rotate 90 degrees counterclockwise
                    Rotation = -MathF.PI / 2;
                    break;
                case Keys.Left:
                case Keys.Up:
                case Keys.Right:
                case Keys.Down: {
                    SizeF direction = key switch {
                        Keys.Left => new(+1, 0),
                        Keys.Right => new(-1, 0),
                        Keys.Down => new(0, -1),
                        Keys.Up => new(0, +1),
                        _ => throw new FailFastException("cannot happen"),
                    };

                    if (!_keys[(byte) Keys.Left].IsHeldForTimer &&
                        !_keys[(byte) Keys.Right].IsHeldForTimer &&
                        !_keys[(byte) Keys.Down].IsHeldForTimer &&
                        !_keys[(byte) Keys.Up].IsHeldForTimer &&
                        !Viewport.WillPanChange(PointF.Add(Viewport.Pan, direction), out _)) {
                        if (_keys[keyb].Press()) {
                            if (direction.Width + direction.Height > 0)
                                NavigateToPrevFile?.Invoke(this, EventArgs.Empty);
                            else
                                NavigateToNextFile?.Invoke(this, EventArgs.Empty);
                        }
                    } else if (_keys[keyb].HoldForTimer()) {
                        _timer.Interval = 1;
                        _timer.Enabled = true;
                    }

                    break;
                }
                case Keys.C when e.Control:
                    (_bitmapSourceTaskCurrent ?? _bitmapSourceTaskPrevious)?.Task.ContinueWith(r => {
                        if (r.IsCompletedSuccessfully)
                            _ = r.Result.SetClipboardImage(UiTaskScheduler);
                    });
                    break;
                case Keys.Multiply:
                case Keys.D8 when e.Shift: // Zoom to 100%
                    if (Math.Abs(Viewport.EffectiveZoom - 1) > 0.000001)
                        Viewport.ScaleMode = new NoZoomScaleMode();
                    else
                        Viewport.ScaleMode = new FitInClientScaleMode(
                            Viewport.Size.Width <= Viewport.ControlBodyWidth &&
                            Viewport.Size.Height <= Viewport.ControlBodyHeight);
                    break;
                case Keys.D9:
                case Keys.D1: // Set default zoom to fit in window
                {
                    if (Viewport.EffectiveScaleMode is IScaleModeWithZoomInToFit sm1)
                        Viewport.DefaultScaleMode = new FitInClientScaleMode(sm1.ZoomInToFit);
                    else if (Viewport.DefaultScaleMode is IScaleModeWithZoomInToFit sm2)
                        Viewport.DefaultScaleMode = new FitInClientScaleMode(sm2.ZoomInToFit);
                    else
                        Viewport.DefaultScaleMode =
                            new FitInClientScaleMode(Viewport.CanPan || Viewport.EffectiveZoom > 1);
                    Viewport.ScaleMode = null;
                    break;
                }
                case Keys.Z: // Toggle zoom-to-fit scale mode
                {
                    if (Viewport.EffectiveScaleMode is FitInClientScaleMode sm1)
                        Viewport.DefaultScaleMode = new FitInClientScaleMode(!sm1.ZoomInToFit);
                    else if (Viewport.EffectiveScaleMode is FitToBorderScaleMode sm2)
                        Viewport.DefaultScaleMode = new FitToBorderScaleMode(!sm2.ZoomInToFit, sm2.DirectionToFit);
                    else
                        Viewport.DefaultScaleMode =
                            new FitInClientScaleMode(Viewport.CanPan || Viewport.EffectiveZoom > 1);
                    Viewport.ScaleMode = null;
                    break;
                }
                case Keys.D7: // Set default zoom to fit height
                case Keys.D8: // Set default zoom to fit width
                {
                    var direction = key == Keys.D7
                        ? FitToBorderScaleMode.Direction.Vertical
                        : FitToBorderScaleMode.Direction.Horizontal;
                    if (Viewport.EffectiveScaleMode is IScaleModeWithZoomInToFit sm1)
                        Viewport.DefaultScaleMode = new FitToBorderScaleMode(sm1.ZoomInToFit, direction);
                    else if (Viewport.DefaultScaleMode is IScaleModeWithZoomInToFit sm2)
                        Viewport.DefaultScaleMode = new FitToBorderScaleMode(sm2.ZoomInToFit, direction);
                    else
                        Viewport.DefaultScaleMode = new FitToBorderScaleMode(
                            Viewport.CanPan || Viewport.EffectiveZoom > 1,
                            direction);
                    Viewport.ScaleMode = null;
                    break;
                }
                case Keys.D0: // Set default zoom to 100%
                    Viewport.DefaultScaleMode = new NoZoomScaleMode();
                    Viewport.ScaleMode = null;
                    break;
                case Keys.Add when e.Control: // Zoom +1% (aligned)
                    Viewport.UpdateZoom((int) Math.Round(100 * Viewport.EffectiveZoom) / 100f + 0.01f);
                    break;
                case Keys.Add: // Zoom +10% (aligned)
                    Viewport.UpdateZoom((int) Math.Round(10 * Viewport.EffectiveZoom) / 10f + 0.1f);
                    break;
                case Keys.Subtract when e.Control: // Zoom -1% (aligned)
                    Viewport.UpdateZoom((int) Math.Round(100 * Viewport.EffectiveZoom) / 100f - 0.01f);
                    break;
                case Keys.Subtract: // Zoom -1% (aligned)
                    Viewport.UpdateZoom((int) Math.Round(10 * Viewport.EffectiveZoom) / 10f - 0.1f);
                    break;
                case Keys.OemOpenBrackets: // Previous image in the set
                    if (_currentImageIndex > 0)
                        ChangeDisplayedMipmap(_currentImageIndex - 1, _currentMipmap);
                    else
                        NavigateToPrevFolder?.Invoke(this, EventArgs.Empty);
                    break;
                case Keys.OemCloseBrackets: // Next image in the set 
                {
                    var count = _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? _bitmapSourceTaskCurrent.Result.ImageCount
                        : 0;
                    if (_currentImageIndex < count - 1)
                        ChangeDisplayedMipmap(_currentImageIndex + 1, _currentMipmap);
                    else
                        NavigateToNextFolder?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case Keys.Oemcomma: // Previous mipmap in the image
                    if (_currentMipmap > 0)
                        ChangeDisplayedMipmap(_currentImageIndex, _currentMipmap - 1);
                    break;
                case Keys.OemPeriod: // Next mipmap in the image
                {
                    var count = _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? _bitmapSourceTaskCurrent.Result.NumberOfMipmaps(_currentImageIndex)
                        : 0;
                    if (_currentMipmap < count - 1)
                        ChangeDisplayedMipmap(_currentImageIndex, _currentMipmap + 1);
                    break;
                }
                case Keys.C: // Toggle background grid
                    TransparencyCellSize = -TransparencyCellSize;
                    break;
                case Keys.T: // Toggle alpha channel; independent from below
                    UseAlphaChannel = !UseAlphaChannel;
                    break;
                case Keys.R: // Show red channel only, or back to showing all channels
                    VisibleColorChannel = VisibleColorChannel == VisibleColorChannelTypes.Red
                        ? VisibleColorChannelTypes.All
                        : VisibleColorChannelTypes.Red;
                    break;
                case Keys.G: // Show green channel only, or back to showing all channels
                    VisibleColorChannel = VisibleColorChannel == VisibleColorChannelTypes.Green
                        ? VisibleColorChannelTypes.All
                        : VisibleColorChannelTypes.Green;
                    break;
                case Keys.B: // Show blue channel only, or back to showing all channels
                    VisibleColorChannel = VisibleColorChannel == VisibleColorChannelTypes.Blue
                        ? VisibleColorChannelTypes.All
                        : VisibleColorChannelTypes.Blue;
                    break;
                case Keys.A: // Show alpha channel only, or back to showing all channels
                    VisibleColorChannel = VisibleColorChannel == VisibleColorChannelTypes.Alpha
                        ? VisibleColorChannelTypes.All
                        : VisibleColorChannelTypes.Alpha;
                    break;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);

        _keys[(byte) RedirectKey(e.KeyCode)].Release();
    }


    private static Keys RedirectKey(Keys k) => k switch {
        Keys.NumPad2 => Keys.Down,
        Keys.NumPad4 => Keys.Left,
        Keys.NumPad6 => Keys.Right,
        Keys.NumPad8 => Keys.Up,
        Keys.Oemplus => Keys.Add,
        Keys.OemMinus => Keys.Subtract,
        _ => k,
    };

    private struct TimedKeyState {
        public bool IsHeld = false;
        public bool IsPressBased = false;
        public bool IsFresh = false;
        public long PressTick = long.MaxValue;
        public long DeltaBaseTick = long.MaxValue;
        public long ReleaseTick = long.MaxValue;

        public TimedKeyState() { }

        public bool IsHeldOrFresh => IsHeld || IsFresh;

        public bool IsHeldForTimer => IsHeld && !IsPressBased;

        /// <summary>
        /// Mark this key as held, for keypress-based event handling mode.
        /// </summary>
        /// <returns>Whether to handle as a keypress event.</returns>
        public bool Press() {
            if (IsHeld)
                return IsPressBased;
            IsPressBased = true;
            IsHeld = true;
            IsFresh = true;
            PressTick = DeltaBaseTick = Environment.TickCount;
            ReleaseTick = long.MaxValue;
            return true;
        }

        /// <summary>
        /// Mark this key as held, for timer-based event handling mode.
        /// </summary>
        /// <returns>Whether to start the timer.</returns>
        public bool HoldForTimer() {
            if (IsHeld)
                return false;
            IsPressBased = false;
            IsHeld = true;
            IsFresh = true;
            PressTick = DeltaBaseTick = Environment.TickCount;
            ReleaseTick = long.MaxValue;
            return true;
        }

        public void Release() {
            if (!IsHeld)
                return;
            IsHeld = false;
            IsFresh = true;
            ReleaseTick = Environment.TickCount64;
        }

        public void ResetAcceleration() {
            IsFresh = false;
            PressTick = DeltaBaseTick = Math.Min(Environment.TickCount64, ReleaseTick);
        }

        public int CalculateAndUpdateDelta() {
            var now = Math.Min(Environment.TickCount64, ReleaseTick);
            var prevElapsedSecs = (DeltaBaseTick - PressTick) / 1000f;
            var newElapsedSecs = (now - PressTick) / 1000f;
            var prevTotal = Math.Pow(prevElapsedSecs, 2) * 1024;
            var newTotal = Math.Pow(newElapsedSecs, 2) * 1024;
            var delta = (int) (newTotal - prevTotal);

            // Make sure that the keypress gets actualized once in case keydown/keyup has happened before
            // a timer event got fired.
            if (delta == 0) {
                if (IsHeld || !IsFresh)
                    return 0;

                delta = 1;
            }

            IsFresh = false;
            DeltaBaseTick = now;
            return delta;
        }
    }
}
