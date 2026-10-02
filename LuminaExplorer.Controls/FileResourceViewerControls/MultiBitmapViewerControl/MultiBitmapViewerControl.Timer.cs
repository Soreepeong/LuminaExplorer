using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private readonly Timer _timer;

    private void TimerOnTick(object? o, EventArgs eventArgs)
    {
        var animating = false;
        var now = Environment.TickCount64;

        var autoDescriptionRemaining = this._autoDescriptionShowUntilTicks - now;
        switch (autoDescriptionRemaining) {
            case < 0:
                this.Invalidate(this.AutoDescriptionRectangle);
                break;
            case < FadeOutDurationMs:
                animating = true;
                this.Invalidate(this.AutoDescriptionRectangle);
                break;
        }

        var overlayRemaining = this._overlayShowUntilTicks - now;
        switch (overlayRemaining) {
            case < 0:
                this.Invalidate();
                break;
            case < FadeOutDurationMs:
                animating = true;
                this.Invalidate();
                break;
        }

        var loadingBoxRemainingUntilShow = this._loadStartTicks == long.MaxValue
            ? int.MaxValue
            : (int) (this._loadStartTicks + this.DelayShowingLoadingBoxFor.TotalMilliseconds - now);

        animating |= this.TimerOnTickProcessPanning();

        if (animating) {
            this._timer.Interval = 1;
            return;
        }

        var next = int.MaxValue;
        if (autoDescriptionRemaining > 0) next = Math.Min(next, (int) (autoDescriptionRemaining - FadeOutDurationMs));
        if (overlayRemaining > 0) next = Math.Min(next, (int) (overlayRemaining - FadeOutDurationMs));
        if (loadingBoxRemainingUntilShow > 0) next = Math.Min(next, loadingBoxRemainingUntilShow);

        if (next == int.MaxValue)
            this._timer.Enabled = false;
        else
            this._timer.Interval = Math.Max(1, next);
    }

    private bool TimerOnTickProcessPanning()
    {
        const byte d = (byte) Keys.Down;
        const byte u = (byte) Keys.Up;
        const byte l = (byte) Keys.Left;
        const byte r = (byte) Keys.Right;

        var now = Environment.TickCount64;
        var panDown = this._keys[d].IsHeldOrFresh;
        var panLeft = this._keys[l].IsHeldOrFresh;
        var panRight = this._keys[r].IsHeldOrFresh;
        var panUp = this._keys[u].IsHeldOrFresh;

        int dx = 0, dy = 0;
        if (panDown && panUp) {
            this._keys[u].ResetAcceleration();
            this._keys[d].ResetAcceleration();
        } else if (panDown) {
            dy -= this._keys[d].CalculateAndUpdateDelta();
        } else if (panUp) {
            dy += this._keys[u].CalculateAndUpdateDelta();
        }

        if (panLeft && panRight) {
            this._keys[r].ResetAcceleration();
            this._keys[l].ResetAcceleration();
        } else if (panRight) {
            dx -= this._keys[r].CalculateAndUpdateDelta();
        } else if (panLeft) {
            dx += this._keys[l].CalculateAndUpdateDelta();
        }

        if (dx != 0 || dy != 0) this.Viewport.Pan = PointF.Add(this.Viewport.Pan, new(dx, dy));

        return panDown || panLeft || panRight || panUp;
    }
}
