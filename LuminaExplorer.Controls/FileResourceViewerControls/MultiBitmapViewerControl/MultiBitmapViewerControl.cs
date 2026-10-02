using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl : AbstractFileResourceViewerControl {
    private const int FadeOutDurationMs = 200;
    private readonly TimeSpan _fadeOutDelay = TimeSpan.FromSeconds(1);

    private int _currentImageIndex;
    private int _currentMipmap;

    public MultiBitmapViewerControl()
    {
        this.ResizeRedraw = true;

        this.MouseActivity.UseLeftDrag = true;
        this.MouseActivity.UseMiddleDrag = true;
        this.MouseActivity.UseRightDrag = true;
        this.MouseActivity.UseLeftDouble = true;
        this.MouseActivity.UseWheelZoom = MouseActivityTracker.WheelZoomMode.RequireControlKey;
        this.MouseActivity.UseDoubleClickDragZoom = true;
        this.MouseActivity.UseInfiniteLeftDrag = true;
        this.MouseActivity.UseInfiniteRightDrag = true;
        this.MouseActivity.UseInfiniteMiddleDrag = true;

        this.MouseActivity.Enabled = false;
        this.Viewport = new(this.MouseActivity);
        this.Viewport.PanExtraRange = new(this.LogicalToDeviceUnits(this._transparencyCellSize * 2));
        this.Viewport.ViewportChanged += this.OnViewportChanged;

        this._timer = new();
        this._timer.Enabled = false;
        this._timer.Interval = 1;
        this._timer.Tick += this.TimerOnTick;

        this.TryGetRenderers(out _, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._bufferedGraphicsContext.Dispose();
            if (this.TryGetRenderers(out var renderers))
                _ = SafeDispose.EnumerableAsync(ref renderers);
            this.Viewport.Dispose();
            this._timer.Dispose();
            _ = SafeDispose.OneAsync(ref this._bitmapSourceTaskCurrent);
            _ = SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
        }

        base.Dispose(disposing);
    }

    public event EventHandler? NavigateToNextFile;

    public event EventHandler? NavigateToPrevFile;

    public event EventHandler? NavigateToNextFolder;

    public event EventHandler? NavigateToPrevFolder;

    protected sealed override void OnPaintBackground(PaintEventArgs e)
    { }

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        var exceptions = Array.Empty<Exception>();
        if (!this.TryGetRenderers(out var renderers, true)) {
            if (this._renderers?.IsFaulted is true)
                exceptions = this._renderers.Exception?.InnerExceptions.ToArray() ??
                    [new("Failed to load any renderer for unknown reasons.")];
        } else {
            if (this.MouseActivity.IsDragging) this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);

            var hasException = false;
            foreach (var r in renderers) {
                if (!r.UpdateBitmapSource(this._bitmapSourceTaskPrevious?.Task, this._bitmapSourceTaskCurrent?.Task) &&
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
            bufferedGraphics = this._bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));

            using var brush = new SolidBrush(this.ForeColor);
            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            if (exceptions.Any()) {
                bufferedGraphics.Graphics.DrawString(
                    $"Error displaying {this.BitmapSource?.FileName}.\n\n" +
                    string.Join('\n', exceptions.Select(x => x.ToString())),
                    this.Font,
                    brush,
                    this.ClientRectangle,
                    stringFormat);
            } else if (this.TryGetEffectiveOverlayInformation(out var overlayText, out _, out _, out _)) {
                bufferedGraphics.Graphics.DrawString(overlayText, this.Font, brush, this.ClientRectangle, stringFormat);
            }
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        this.Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var nowHovers = this.AutoDescriptionRectangle.Contains(e.Location);
        if (this._autoDescriptionBeingHovered != nowHovers) {
            this._autoDescriptionBeingHovered = nowHovers;
            if (nowHovers)
                this.Invalidate(this.AutoDescriptionRectangle);
            else
                this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (this._autoDescriptionBeingHovered) {
            this._autoDescriptionBeingHovered = false;
            this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (0 == (ModifierKeys & Keys.Modifiers) && !this.MouseActivity.IsDragging) {
            if (e.Delta > 0)
                this.NavigateToPrevFile?.Invoke(this, EventArgs.Empty);
            else
                this.NavigateToNextFile?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);

        // need to mutate; no foreach
        for (var i = 0; i < this._keys.Length; i++) this._keys[i].Release();
    }

    protected override void OnMarginChanged(EventArgs e)
    {
        base.OnMarginChanged(e);
        this.Invalidate();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        this.Invalidate();
    }
}
