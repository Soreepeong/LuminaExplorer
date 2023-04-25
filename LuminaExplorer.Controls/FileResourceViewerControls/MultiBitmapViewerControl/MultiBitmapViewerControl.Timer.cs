using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private readonly Timer _timer;
    
    private void TimerOnTick(object? o, EventArgs eventArgs) {
        var animating = false;
        var now = Environment.TickCount64;

        var autoDescriptionRemaining = _autoDescriptionShowUntilTicks - now;
        switch (autoDescriptionRemaining) {
            case < 0:
                Invalidate(AutoDescriptionRectangle);
                break;
            case < FadeOutDurationMs:
                animating = true;
                Invalidate(AutoDescriptionRectangle);
                break;
        }

        var overlayRemaining = _overlayShowUntilTicks - now;
        switch (overlayRemaining) {
            case < 0:
                Invalidate();
                break;
            case < FadeOutDurationMs:
                animating = true;
                Invalidate();
                break;
        }

        var loadingBoxRemainingUntilShow = _loadStartTicks == long.MaxValue
            ? int.MaxValue
            : (int) (_loadStartTicks + DelayShowingLoadingBoxFor.TotalMilliseconds - now);

        animating |= TimerOnTickProcessPanning();

        if (animating) {
            _timer.Interval = 1;
            return;
        }

        var next = int.MaxValue;
        if (autoDescriptionRemaining > 0) next = Math.Min(next, (int) (autoDescriptionRemaining - FadeOutDurationMs));
        if (overlayRemaining > 0) next = Math.Min(next, (int) (overlayRemaining - FadeOutDurationMs));
        if (loadingBoxRemainingUntilShow > 0) next = Math.Min(next, loadingBoxRemainingUntilShow);

        if (next == int.MaxValue)
            _timer.Enabled = false;
        else
            _timer.Interval = next;
    }

    private bool TimerOnTickProcessPanning() {
        const byte d = (byte) Keys.Down;
        const byte u = (byte) Keys.Up;
        const byte l = (byte) Keys.Left;
        const byte r = (byte) Keys.Right;
        
        var now = Environment.TickCount64;
        var panDown = _keys[d].IsHeldOrFresh;
        var panLeft = _keys[l].IsHeldOrFresh;
        var panRight = _keys[r].IsHeldOrFresh;
        var panUp = _keys[u].IsHeldOrFresh;

        int dx = 0, dy = 0;
        if (panDown && panUp) {
            _keys[u].ResetAcceleration();
            _keys[d].ResetAcceleration();
        } else if (panDown) {
            dy -= _keys[d].CalculateAndUpdateDelta();
        } else if (panUp) {
            dy += _keys[u].CalculateAndUpdateDelta();
        }
        
        if (panLeft && panRight) {
            _keys[r].ResetAcceleration();
            _keys[l].ResetAcceleration();
        } else if (panRight) {
            dx -= _keys[r].CalculateAndUpdateDelta();
        } else if (panLeft) {
            dx += _keys[l].CalculateAndUpdateDelta();
        }

        if (dx != 0 || dy != 0)
            Viewport.Pan = PointF.Add(Viewport.Pan, new(dx, dy));

        return panDown || panLeft || panRight || panUp;
    }
}
