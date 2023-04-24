using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;

internal sealed class GdipTexRenderer : ITexRenderer {
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();
    private readonly Task<IBitmapSource>?[] _sources = new Task<IBitmapSource>?[2];
    
    private RectangleF? _autoDescriptionRectangle;

    public GdipTexRenderer(TexFileViewerControl control) {
        Control = control;
        Control.Resize += ControlOnResize;
        Control.FontSizeStepLevelChanged += ControlOnFontSizeStepLevelChanged;
    }

    public void UiThreadInitialize() { }

    public void Dispose() {
        UpdateBitmapSource(null, null);
        Control.Resize -= ControlOnResize;
        Control.FontSizeStepLevelChanged -= ControlOnFontSizeStepLevelChanged;
        _bufferedGraphicsContext.Dispose();
    }

    private Task<IBitmapSource>? SourceTaskPrevious {
        get => _sources[0];
        set => _sources[0] = value;
    }

    private Task<IBitmapSource>? SourceTaskCurrent {
        get => _sources[1];
        set => _sources[1] = value;
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;

    private TexFileViewerControl Control { get; }

    public Exception? LastException { get; private set; }

    public RectangleF? AutoDescriptionRectangle {
        get {
            if (_autoDescriptionRectangle is not null)
                return _autoDescriptionRectangle.Value;
            var padding = Control.Padding;
            var margin = Control.Margin;
            var rc = new Rectangle(
                padding.Left + margin.Left,
                padding.Top + margin.Top,
                Control.ClientSize.Width - padding.Horizontal - margin.Horizontal,
                Control.ClientSize.Height - padding.Vertical - margin.Vertical);

            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.None,
            };

            using var g = Control.CreateGraphics();
            var controlFont = Control.Font;
            using var font = new Font(
                controlFont.FontFamily,
                Control.EffectiveFontSizeInPoints,
                controlFont.Style, 
                controlFont.Unit,
                controlFont.GdiCharSet,
                controlFont.GdiVerticalFont);
            var measured = g.MeasureString(
                Control.AutoDescription,
                font,
                new SizeF(rc.Width, rc.Height),
                stringFormat);
            rc.Width = (int) Math.Ceiling(measured.Width);
            rc.Height = (int) Math.Ceiling(measured.Height);
            return _autoDescriptionRectangle = rc;
        }
        set => _autoDescriptionRectangle = value;
    }

    private LoadState TryGetActiveSource(out IBitmapSource source, out Exception? exception) {
        source = null!;
        exception = null;
        var hasLoading = false;

        foreach (var bitmapSourceTask in new[] {SourceTaskCurrent, SourceTaskPrevious}) {
            if (bitmapSourceTask?.IsCompletedSuccessfully is not true) {
                if (exception is null && bitmapSourceTask?.IsFaulted is true)
                    exception = bitmapSourceTask.Exception;
                else
                    hasLoading = true;
                continue;
            }

            source = bitmapSourceTask.Result;
            return LoadState.Loaded;
        }

        if (hasLoading)
            return LoadState.Loading;

        return exception is not null ? LoadState.Error : LoadState.Empty;
    }

    public bool UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current) {
        var changed = false;
        LastException = null;

        if (previous == current)
            previous = null;

        changed |= SourceTaskPrevious != previous;
        SourceTaskPrevious = previous;

        if (SourceTaskCurrent != current) {
            changed = true;
            SourceTaskCurrent?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully) {
                    r.Result.LayoutChanged -= BitmapSourceCurrentOnLayoutChanged;
                    BitmapSourceCurrentOnLayoutChanged();
                }
            });
            SourceTaskCurrent = current;
            SourceTaskCurrent?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.LayoutChanged += BitmapSourceCurrentOnLayoutChanged;
            });
        }

        return changed;
    }

    private void BitmapSourceCurrentOnLayoutChanged() {
        if (SourceTaskCurrent is not { } b)
            return;
        b.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully)
                return;
            r.Result.GetGdipBitmapAsync(r.Result.Layout[0])
                .ContinueWith(_ => AnyBitmapSourceSliceAvailableForDrawing?.Invoke(b));
        });
    }

    public bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask)
        => bitmapSourceTask?.IsCompletedSuccessfully is true &&
           bitmapSourceTask.Result.Layout.Any(bitmapSourceTask.Result.HasGdipBitmap) &&
           (bitmapSourceTask == SourceTaskCurrent || bitmapSourceTask == SourceTaskPrevious);

    public bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask)
        => bitmapSourceTask?.IsCompletedSuccessfully is true &&
           bitmapSourceTask.Result.Layout.All(bitmapSourceTask.Result.HasGdipBitmap) &&
           (bitmapSourceTask == SourceTaskCurrent || bitmapSourceTask == SourceTaskPrevious);

    public bool Draw(PaintEventArgs e) {
        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            var g = bufferedGraphics.Graphics;

            if (!Control.TryGetEffectiveOverlayInformation(
                    out var overlayString,
                    out var overlayForeOpacity,
                    out var overlayBackOpacity,
                    out var hideIfNotLoading))
                overlayString = null;

            if (IsAnyVisibleSliceReadyForDrawing(SourceTaskCurrent))
                g.Clear(Control.BackColorWhenLoaded);
            else if (SourceTaskCurrent?.IsFaulted is true)
                g.Clear(Control.BackColor);
            else if (IsAnyVisibleSliceReadyForDrawing(SourceTaskPrevious))
                g.Clear(Control.BackColorWhenLoaded);
            else
                g.Clear(Control.BackColor);

            var currentSourceFullyAvailable = IsEveryVisibleSliceReadyForDrawing(SourceTaskCurrent);
            var isLoading = false;
            foreach (var sourceTask in _sources) {
                if (sourceTask is null)
                    continue;
                if (currentSourceFullyAvailable && SourceTaskPrevious == sourceTask) {
                    Debug.Assert(SourceTaskPrevious != SourceTaskCurrent);
                    continue;
                }

                if (sourceTask.IsCompletedSuccessfully) {
                    var source = sourceTask.Result;
                    var imageRect = Control.ImageRect;
                    var clientSize = Control.ClientSize;
                    var overlayRect = new Rectangle(
                        Control.Padding.Left + Control.Margin.Left,
                        Control.Padding.Top + Control.Margin.Top,
                        clientSize.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
                        clientSize.Height - Control.Padding.Vertical - Control.Margin.Vertical);

                    g.InterpolationMode = Control.NearestNeighborMinimumZoom <= Control.EffectiveZoom
                        ? InterpolationMode.NearestNeighbor
                        : InterpolationMode.Bilinear;

                    // 1. Draw transparency grids
                    foreach (var sliceCell in source.Layout) {
                        var cellRect = source.Layout.RectOf(sliceCell, imageRect);

                        if (Control.ShouldDrawTransparencyGrid(
                                cellRect,
                                e.ClipRectangle,
                                out var multiplier,
                                out var minX,
                                out var minY,
                                out var dx,
                                out var dy)) {
                            using var cellBrush1 = new SolidBrush(Control.TransparencyCellColor1);
                            using var cellBrush2 = new SolidBrush(Control.TransparencyCellColor2);
                            var yLim = Math.Min(cellRect.Bottom, e.ClipRectangle.Bottom);
                            var xLim = Math.Min(cellRect.Right, e.ClipRectangle.Right);
                            var rc = new RectangleF();
                            for (var y = minY;; y++) {
                                rc.Y = y * multiplier + dy;
                                rc.Height = Math.Min(multiplier, yLim - rc.Y);
                                if (rc.Height <= 0)
                                    break;

                                for (var x = minX;; x++) {
                                    rc.X = x * multiplier + dx;
                                    rc.Width = Math.Min(multiplier, xLim - rc.X);
                                    if (rc.Width <= 0)
                                        break;

                                    g.FillRectangle((x + y) % 2 == 0 ? cellBrush1 : cellBrush2, rc);
                                }
                            }
                        }
                    }

                    // 2. Draw cell borders
                    var contentBorderWidth = Control.ContentBorderWidth;
                    if (contentBorderWidth > 0) {
                        using var borderPen = new Pen(Control.ContentBorderColor, Control.ContentBorderWidth);
                        foreach (var sliceCell in source.Layout) {
                            g.DrawRectangle(borderPen, RectangleF.Inflate(
                                source.Layout.RectOf(sliceCell, imageRect),
                                contentBorderWidth / 2f,
                                contentBorderWidth / 2f));
                        }
                    }

                    // 3. Draw bitmaps
                    foreach (var sliceCell in source.Layout) {
                        var bitmapTask = source.GetGdipBitmapAsync(sliceCell);
                        if (bitmapTask.IsCompletedSuccessfully) {
                            g.DrawImage(bitmapTask.Result, source.Layout.RectOf(sliceCell, imageRect));
                        } else if (!bitmapTask.IsFaulted) {
                            // TODO: draw loading
                        } else {
                            // TODO: draw error
                        }
                    }

                    // skip pixel grid for gdip mode

                    DrawText(
                        g,
                        Control.AutoDescription,
                        overlayRect,
                        StringAlignment.Near,
                        StringAlignment.Near,
                        Control.Font,
                        Control.ForeColorWhenLoaded,
                        Control.BackColorWhenLoaded,
                        Control.AutoDescriptionOpacity,
                        2);
                } else if (sourceTask.IsFaulted)
                    overlayString = $"Error occurred loading the file.\n{sourceTask.Exception}";
                else
                    isLoading = true;
            }

            if (hideIfNotLoading && !isLoading)
                overlayString = null;

            if (overlayString is null)
                return true;

            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
            };

            var size = g.MeasureString(overlayString, Control.Font, Control.ClientSize, stringFormat);
            var box = new Rectangle(
                Control.ClientSize.Width / 2 - (int) size.Width / 2 - 32,
                Control.ClientSize.Height / 2 - (int) size.Height / 2 - 32,
                (int) size.Width + 64,
                (int) size.Height + 64);

            using (var backBrush = new SolidBrush(Control.BackColor.MultiplyOpacity(overlayBackOpacity)))
                g.FillRectangle(backBrush, box);

            DrawText(
                g,
                overlayString,
                box,
                StringAlignment.Center,
                StringAlignment.Center,
                Control.Font,
                Control.ForeColor,
                Control.BackColor,
                overlayForeOpacity,
                2);

            return true;
        } catch (Exception ex) {
            LastException = ex;
            return false;
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    private void DrawText(
        Graphics g,
        string? @string,
        Rectangle rectangle,
        StringAlignment alignment,
        StringAlignment lineAlignment,
        Font font,
        Color textColor,
        Color shadowColor,
        float opacity = 1f,
        int borderWidth = 0) {
        if (opacity <= 0 || string.IsNullOrEmpty(@string))
            return;

        using var stringFormat = new StringFormat {
            Alignment = alignment,
            LineAlignment = lineAlignment,
            Trimming = StringTrimming.None,
        };

        using var shadowBrush = new SolidBrush(shadowColor.MultiplyOpacity(opacity));
        for (var i = -borderWidth; i <= borderWidth; i++) {
            for (var j = -borderWidth; j <= borderWidth; j++) {
                if (i == 0 && j == 0)
                    continue;
                g.DrawString(
                    @string,
                    font,
                    shadowBrush,
                    rectangle with {X = rectangle.X + i, Y = rectangle.Y + j},
                    stringFormat);
            }
        }

        using var textBrush = new SolidBrush(textColor.MultiplyOpacity(opacity));
        g.DrawString(
            @string,
            font,
            textBrush,
            rectangle,
            stringFormat);
    }

    private void ControlOnFontSizeStepLevelChanged(object? sender, EventArgs e) {
        _autoDescriptionRectangle = null;
    }

    private void ControlOnResize(object? sender, EventArgs e) {
        _autoDescriptionRectangle = null;
    }
}
