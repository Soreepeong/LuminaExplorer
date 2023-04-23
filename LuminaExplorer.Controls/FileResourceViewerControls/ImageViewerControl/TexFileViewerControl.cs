using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util.ScaleMode;
using LuminaExplorer.Core.Util;
using Timer = System.Windows.Forms.Timer;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private const int FadeOutDurationMs = 200;
    private readonly TimeSpan _fadeOutDelay = TimeSpan.FromSeconds(1);

    private int _currentImageIndex;
    private int _currentMipmap;
    private readonly Timer _fadeTimer;

    public TexFileViewerControl() {
        ResizeRedraw = true;

        MouseActivity.UseLeftDrag = true;
        MouseActivity.UseMiddleDrag = true;
        MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;
        MouseActivity.UseInfiniteLeftDrag = true;
        MouseActivity.UseInfiniteRightDrag = true;
        MouseActivity.UseInfiniteMiddleDrag = true;

        MouseActivity.Enabled = false;
        Viewport = new(MouseActivity);
        Viewport.PanExtraRange = new(_transparencyCellSize * 2);
        Viewport.ViewportChanged += () => {
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
            Invalidate();
        };

        _fadeTimer = new();
        _fadeTimer.Enabled = false;
        _fadeTimer.Interval = 1;
        _fadeTimer.Tick += OnFadeTimerOnTick;

        TryGetRenderers(out _, true);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _bufferedGraphicsContext.Dispose();
            if (TryGetRenderers(out var renderers))
                _ = SafeDispose.EnumerableAsync(ref renderers);
            Viewport.Dispose();
            _fadeTimer.Dispose();
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
        }

        base.Dispose(disposing);
    }

    public FileInfo? PhysicalFile { get; private set; }

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
                if (r.LastException is not null) {
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
                    $"Error displaying {FileName}.\n\n" +
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

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!_autoDescriptionBeingHovered) {
            if (AutoDescriptionRectangle.Contains(e.Location)) {
                _autoDescriptionBeingHovered = true;
                Invalidate(AutoDescriptionRectangle);
            }
        } else {
            _autoDescriptionBeingHovered = false;
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

    protected override void OnKeyDown(KeyEventArgs e) {
        if (MouseActivity.Enabled) {
            switch (e.KeyCode) {
                case Keys.C when e.Control: // TODO: Copy
                    break;
                case Keys.Multiply:
                case Keys.D8 when e.Shift: // Zoom to 100%
                    Viewport.UpdateScaleMode(new NoZoomScaleMode());
                    break;
                case Keys.Oemplus when e.Control: // Zoom +1% (aligned)
                case Keys.Add when e.Control:
                    Viewport.UpdateZoom((int) Math.Round(100 * Viewport.EffectiveZoom) / 100f + 0.01f);
                    break;
                case Keys.Oemplus: // Zoom +10% (aligned)
                case Keys.Add:
                    Viewport.UpdateZoom((int) Math.Round(10 * Viewport.EffectiveZoom) / 10f + 0.1f);
                    break;
                case Keys.OemMinus when e.Control: // Zoom -1% (aligned)
                case Keys.Subtract when e.Control:
                    Viewport.UpdateZoom((int) Math.Round(100 * Viewport.EffectiveZoom) / 100f - 0.01f);
                    break;
                case Keys.OemMinus: // Zoom -1% (aligned)
                case Keys.Subtract:
                    Viewport.UpdateZoom((int) Math.Round(10 * Viewport.EffectiveZoom) / 10f - 0.1f);
                    break;
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
                case Keys.OemOpenBrackets: // Previous image in the set
                    if (_currentImageIndex > 0)
                        ChangeDisplayedMipmap(_currentImageIndex - 1, _currentMipmap);
                    // TODO: event: previous folder
                    break;
                case Keys.OemCloseBrackets: // Next image in the set 
                {
                    var count = _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? _bitmapSourceTaskCurrent.Result.ImageCount
                        : 0;
                    if (_currentImageIndex < count - 1)
                        ChangeDisplayedMipmap(_currentImageIndex + 1, _currentMipmap);
                    // TODO: event: next folder
                    break;
                }
                case Keys.Oemcomma: // Previous mipmap in the image
                    if (_currentMipmap > 0)
                        ChangeDisplayedMipmap(_currentImageIndex, _currentMipmap - 1);
                    break;
                case Keys.OemPeriod: // Next mipmap in the image
                {
                    var count = _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                        ? _bitmapSourceTaskCurrent.Result.NumMipmaps
                        : 0;
                    if (_currentMipmap < count - 1)
                        ChangeDisplayedMipmap(_currentImageIndex, _currentMipmap + 1);
                    break;
                }
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

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e) {
        base.OnKeyUp(e);
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
