using System;
using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util.ScaleMode;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private readonly TimedKeyState[] _keys = new TimedKeyState[256];

    /// <summary>
    /// Performs the action bound to the given hotkey, as if the key was pressed and released.
    /// </summary>
    /// <param name="keyData">Key and modifiers.</param>
    public void PerformHotkey(Keys keyData)
    {
        this.OnKeyDown(new(keyData));
        this.OnKeyUp(new(keyData));
    }

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode) {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
                return true;
        }

        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var key = RedirectKey(e.KeyCode);
        var keyb = (byte) key;
        if (this.MouseActivity.Enabled) {
            switch (key) {
                case Keys.Up when e.Alt: // Disable rotation
                    this.Rotation = 0;
                    break;
                case Keys.Right when e.Alt: // Rotate 90 degrees clockwise
                    this.Rotation = MathF.PI / 2;
                    break;
                case Keys.Down when e.Alt: // Rotate 180 degrees
                    this.Rotation = MathF.PI;
                    break;
                case Keys.Left when e.Alt: // Rotate 90 degrees counterclockwise
                    this.Rotation = -MathF.PI / 2;
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

                    if (!this._keys[(byte) Keys.Left].IsHeldForTimer &&
                        !this._keys[(byte) Keys.Right].IsHeldForTimer &&
                        !this._keys[(byte) Keys.Down].IsHeldForTimer &&
                        !this._keys[(byte) Keys.Up].IsHeldForTimer &&
                        !this.Viewport.WillPanChange(PointF.Add(this.Viewport.Pan, direction), out _)) {
                        if (this._keys[keyb].Press()) {
                            if (direction.Width + direction.Height > 0)
                                this.NavigateToPrevFile?.Invoke(this, EventArgs.Empty);
                            else
                                this.NavigateToNextFile?.Invoke(this, EventArgs.Empty);
                        }
                    } else if (this._keys[keyb].HoldForTimer()) {
                        this._timer.Interval = 1;
                        this._timer.Enabled = true;
                    }

                    break;
                }
                case Keys.C when e.Control:
                    (this._bitmapSourceTaskCurrent ?? this._bitmapSourceTaskPrevious)?.Task.ContinueWith(
                        r => {
                            if (r.IsCompletedSuccessfully)
                                _ = r.Result.SetClipboardImage(this.UiTaskScheduler);
                        });
                    break;
                case Keys.Multiply:
                case Keys.D8 when e.Shift: // Zoom to 100%
                    if (Math.Abs(this.Viewport.EffectiveZoom - 1) > 0.000001)
                        this.Viewport.ScaleMode = new NoZoomScaleMode();
                    else
                        this.Viewport.ScaleMode = new FitInClientScaleMode(
                            this.Viewport.Size.Width <= this.Viewport.ControlBodyWidth &&
                            this.Viewport.Size.Height <= this.Viewport.ControlBodyHeight);
                    break;
                case Keys.D9:
                case Keys.D1: // Set default zoom to fit in window
                {
                    if (this.Viewport.EffectiveScaleMode is IScaleModeWithZoomInToFit sm1)
                        this.Viewport.DefaultScaleMode = new FitInClientScaleMode(sm1.ZoomInToFit);
                    else if (this.Viewport.DefaultScaleMode is IScaleModeWithZoomInToFit sm2)
                        this.Viewport.DefaultScaleMode = new FitInClientScaleMode(sm2.ZoomInToFit);
                    else
                        this.Viewport.DefaultScaleMode =
                            new FitInClientScaleMode(this.Viewport.CanPan || this.Viewport.EffectiveZoom > 1);
                    this.Viewport.ScaleMode = null;
                    break;
                }
                case Keys.Z: // Toggle zoom-to-fit scale mode
                {
                    if (this.Viewport.EffectiveScaleMode is FitInClientScaleMode sm1)
                        this.Viewport.DefaultScaleMode = new FitInClientScaleMode(!sm1.ZoomInToFit);
                    else if (this.Viewport.EffectiveScaleMode is FitToBorderScaleMode sm2)
                        this.Viewport.DefaultScaleMode = new FitToBorderScaleMode(!sm2.ZoomInToFit, sm2.DirectionToFit);
                    else
                        this.Viewport.DefaultScaleMode =
                            new FitInClientScaleMode(this.Viewport.CanPan || this.Viewport.EffectiveZoom > 1);
                    this.Viewport.ScaleMode = null;
                    break;
                }
                case Keys.D7: // Set default zoom to fit height
                case Keys.D8: // Set default zoom to fit width
                {
                    var direction = key == Keys.D7
                        ? FitToBorderScaleMode.Direction.Vertical
                        : FitToBorderScaleMode.Direction.Horizontal;
                    if (this.Viewport.EffectiveScaleMode is IScaleModeWithZoomInToFit sm1)
                        this.Viewport.DefaultScaleMode = new FitToBorderScaleMode(sm1.ZoomInToFit, direction);
                    else if (this.Viewport.DefaultScaleMode is IScaleModeWithZoomInToFit sm2)
                        this.Viewport.DefaultScaleMode = new FitToBorderScaleMode(sm2.ZoomInToFit, direction);
                    else
                        this.Viewport.DefaultScaleMode = new FitToBorderScaleMode(
                            this.Viewport.CanPan || this.Viewport.EffectiveZoom > 1,
                            direction);
                    this.Viewport.ScaleMode = null;
                    break;
                }
                case Keys.D0: // Set default zoom to 100%
                    this.Viewport.DefaultScaleMode = new NoZoomScaleMode();
                    this.Viewport.ScaleMode = null;
                    break;
                case Keys.Add when e.Control: // Zoom +1% (aligned)
                    this.Viewport.UpdateZoom((int) Math.Round(100 * this.Viewport.EffectiveZoom) / 100f + 0.01f);
                    break;
                case Keys.Add: // Zoom +10% (aligned)
                    this.Viewport.UpdateZoom((int) Math.Round(10 * this.Viewport.EffectiveZoom) / 10f + 0.1f);
                    break;
                case Keys.Subtract when e.Control: // Zoom -1% (aligned)
                    this.Viewport.UpdateZoom((int) Math.Round(100 * this.Viewport.EffectiveZoom) / 100f - 0.01f);
                    break;
                case Keys.Subtract: // Zoom -1% (aligned)
                    this.Viewport.UpdateZoom((int) Math.Round(10 * this.Viewport.EffectiveZoom) / 10f - 0.1f);
                    break;
                case Keys.OemOpenBrackets: // Previous image in the set
                    if (this._currentImageIndex > 0)
                        this.ChangeDisplayedMipmap(this._currentImageIndex - 1, this._currentMipmap);
                    else
                        this.NavigateToPrevFolder?.Invoke(this, EventArgs.Empty);
                    break;
                case Keys.OemCloseBrackets: // Next image in the set 
                {
                    var count = this._bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? this._bitmapSourceTaskCurrent.Result.ImageCount
                        : 0;
                    if (this._currentImageIndex < count - 1)
                        this.ChangeDisplayedMipmap(this._currentImageIndex + 1, this._currentMipmap);
                    else
                        this.NavigateToNextFolder?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case Keys.Oemcomma: // Previous mipmap in the image
                    if (this._currentMipmap > 0)
                        this.ChangeDisplayedMipmap(this._currentImageIndex, this._currentMipmap - 1);
                    break;
                case Keys.OemPeriod: // Next mipmap in the image
                {
                    var count = this._bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? this._bitmapSourceTaskCurrent.Result.NumberOfMipmaps(this._currentImageIndex)
                        : 0;
                    if (this._currentMipmap < count - 1)
                        this.ChangeDisplayedMipmap(this._currentImageIndex, this._currentMipmap + 1);
                    break;
                }
                case Keys.C: // Toggle background grid
                    this.TransparencyCellSize = -this.TransparencyCellSize;
                    break;
                case Keys.T: // Toggle alpha channel; independent from below
                    if (this.ChannelFilter == DirectXTexRendererShader.VisibleColorChannelTypes.Alpha)
                        this.ChannelFilter = DirectXTexRendererShader.VisibleColorChannelTypes.All;
                    else
                        this.UseAlphaChannel = !this.UseAlphaChannel;
                    break;
                case Keys.R: // Show red channel only, or back to showing all channels
                    this.ChannelFilter = this.ChannelFilter == DirectXTexRendererShader.VisibleColorChannelTypes.Red
                        ? DirectXTexRendererShader.VisibleColorChannelTypes.All
                        : DirectXTexRendererShader.VisibleColorChannelTypes.Red;
                    break;
                case Keys.G: // Show green channel only, or back to showing all channels
                    this.ChannelFilter = this.ChannelFilter == DirectXTexRendererShader.VisibleColorChannelTypes.Green
                        ? DirectXTexRendererShader.VisibleColorChannelTypes.All
                        : DirectXTexRendererShader.VisibleColorChannelTypes.Green;
                    break;
                case Keys.B: // Show blue channel only, or back to showing all channels
                    this.ChannelFilter = this.ChannelFilter == DirectXTexRendererShader.VisibleColorChannelTypes.Blue
                        ? DirectXTexRendererShader.VisibleColorChannelTypes.All
                        : DirectXTexRendererShader.VisibleColorChannelTypes.Blue;
                    break;
                case Keys.A: // Show alpha channel only, or back to showing all channels
                    this.ChannelFilter = this.ChannelFilter == DirectXTexRendererShader.VisibleColorChannelTypes.Alpha
                        ? DirectXTexRendererShader.VisibleColorChannelTypes.All
                        : DirectXTexRendererShader.VisibleColorChannelTypes.Alpha;
                    break;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        this._keys[(byte) RedirectKey(e.KeyCode)].Release();
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

        public TimedKeyState()
        { }

        public bool IsHeldOrFresh => this.IsHeld || this.IsFresh;

        public bool IsHeldForTimer => this.IsHeld && !this.IsPressBased;

        /// <summary>
        /// Mark this key as held, for keypress-based event handling mode.
        /// </summary>
        /// <returns>Whether to handle as a keypress event.</returns>
        public bool Press()
        {
            if (this.IsHeld)
                return this.IsPressBased;
            this.IsPressBased = true;
            this.IsHeld = true;
            this.IsFresh = true;
            this.PressTick = this.DeltaBaseTick = Environment.TickCount64;
            this.ReleaseTick = long.MaxValue;
            return true;
        }

        /// <summary>
        /// Mark this key as held, for timer-based event handling mode.
        /// </summary>
        /// <returns>Whether to start the timer.</returns>
        public bool HoldForTimer()
        {
            if (this.IsHeld)
                return false;
            this.IsPressBased = false;
            this.IsHeld = true;
            this.IsFresh = true;
            this.PressTick = this.DeltaBaseTick = Environment.TickCount64;
            this.ReleaseTick = long.MaxValue;
            return true;
        }

        public void Release()
        {
            if (!this.IsHeld)
                return;
            this.IsHeld = false;
            this.IsFresh = true;
            this.ReleaseTick = Environment.TickCount64;
        }

        public void ResetAcceleration()
        {
            this.IsFresh = false;
            this.PressTick = this.DeltaBaseTick = Math.Min(Environment.TickCount64, this.ReleaseTick);
        }

        public int CalculateAndUpdateDelta()
        {
            var now = Math.Min(Environment.TickCount64, this.ReleaseTick);
            var prevElapsedSecs = (this.DeltaBaseTick - this.PressTick) / 1000f;
            var newElapsedSecs = (now - this.PressTick) / 1000f;
            var prevTotal = MathF.Pow(0.5f + prevElapsedSecs, 4) * 1024;
            var newTotal = MathF.Pow(0.5f + newElapsedSecs, 4) * 1024;
            var delta = (int) (newTotal - prevTotal);

            // Make sure that the keypress gets actualized once in case keydown/keyup has happened before
            // a timer event got fired.
            if (delta == 0) {
                if (this.IsHeld || !this.IsFresh)
                    return 0;

                delta = 1;
            }

            this.IsFresh = false;
            this.DeltaBaseTick = now;
            return delta;
        }
    }
}
