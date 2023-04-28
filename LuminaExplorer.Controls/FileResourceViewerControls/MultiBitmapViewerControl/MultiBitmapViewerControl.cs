using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl : AbstractFileResourceViewerControl {
    private const int FadeOutDurationMs = 200;
    private readonly TimeSpan _fadeOutDelay = TimeSpan.FromSeconds(1);

    private int _currentImageIndex;
    private int _currentMipmap;

    public MultiBitmapViewerControl() {
        ResizeRedraw = true;

        MouseActivity.UseLeftDrag = true;
        MouseActivity.UseMiddleDrag = true;
        MouseActivity.UseRightDrag = true;
        MouseActivity.UseLeftDouble = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;
        MouseActivity.UseInfiniteLeftDrag = true;
        MouseActivity.UseInfiniteRightDrag = true;
        MouseActivity.UseInfiniteMiddleDrag = true;

        MouseActivity.Enabled = false;
        Viewport = new(MouseActivity);
        Viewport.PanExtraRange = new(_transparencyCellSize * 2);
        Viewport.ViewportChanged += OnViewportChanged;

        _timer = new();
        _timer.Enabled = false;
        _timer.Interval = 1;
        _timer.Tick += TimerOnTick;

        TryGetRenderers(out _, true);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _bufferedGraphicsContext.Dispose();
            if (TryGetRenderers(out var renderers))
                _ = SafeDispose.EnumerableAsync(ref renderers);
            Viewport.Dispose();
            _timer.Dispose();
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
        }

        base.Dispose(disposing);
    }

    public event EventHandler? NavigateToNextFile;

    public event EventHandler? NavigateToPrevFile;

    public event EventHandler? NavigateToNextFolder;

    public event EventHandler? NavigateToPrevFolder;

    protected sealed override void OnPaintBackground(PaintEventArgs e) { }

    protected sealed override void OnPaint(PaintEventArgs e) {
        var exceptions = Array.Empty<Exception>();
        if (!TryGetRenderers(out var renderers, true)) {
            if (_renderers?.IsFaulted is true)
                exceptions = _renderers.Exception?.InnerExceptions.ToArray() ??
                             new Exception[] {new("Failed to load any renderer for unknown reasons.")};
        } else {
            if (MouseActivity.IsDragging)
                ExtendDescriptionMandatoryDisplay(_fadeOutDelay);

            var hasException = false;
            foreach (var r in renderers) {
                if (!r.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task) &&
                    r.LastException is not null) {
                    hasException = true;
                    continue;
                }

                if (r.Draw(e))
                    return;
            }

            if (hasException)
                exceptions = renderers.Where(x => x.LastException is not null).Select(x => x.LastException!).ToArray();
        }

        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));

            using var brush = new SolidBrush(ForeColor);
            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            if (exceptions.Any()) {
                bufferedGraphics.Graphics.DrawString(
                    $"Error displaying {BitmapSource?.FileName}.\n\n" +
                    string.Join('\n', exceptions.Select(x => x.ToString())),
                    Font,
                    brush,
                    ClientRectangle,
                    stringFormat);
            } else if (TryGetEffectiveOverlayInformation(out var overlayText, out _, out _, out _)) {
                bufferedGraphics.Graphics.DrawString(overlayText, Font, brush, ClientRectangle, stringFormat);
            }
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);

        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        
        var nowHovers = AutoDescriptionRectangle.Contains(e.Location);
        if (_autoDescriptionBeingHovered != nowHovers) {
            _autoDescriptionBeingHovered = nowHovers;
            if (nowHovers)
                Invalidate(AutoDescriptionRectangle);
            else
                ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        }
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_autoDescriptionBeingHovered) {
            _autoDescriptionBeingHovered = false;
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        if (0 == (ModifierKeys & Keys.Modifiers) && !MouseActivity.IsDragging) {
            if (e.Delta > 0)
                NavigateToPrevFile?.Invoke(this, EventArgs.Empty);
            else
                NavigateToNextFile?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnLostFocus(EventArgs e) {
        base.OnLostFocus(e);

        // need to mutate; no foreach
        for (var i = 0; i < _keys.Length; i++)
            _keys[i].Release();
    }

    protected override void OnMarginChanged(EventArgs e) {
        base.OnMarginChanged(e);
        Invalidate();
    }

    protected override void OnPaddingChanged(EventArgs e) {
        base.OnPaddingChanged(e);
        Invalidate();
    }

    public enum VisibleColorChannelTypes {
        All,
        Red,
        Green,
        Blue,
        Alpha,
    }
}
