using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;

internal sealed class GdipTexRenderer : ITexRenderer {
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();
    private readonly Task<IBitmapSource>?[] _sources = new Task<IBitmapSource>?[2];

    private RectangleF? _autoDescriptionRectangle;

    public GdipTexRenderer(MultiBitmapViewerControl control) {
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

    public Task<IBitmapSource>? PreviousSourceTask {
        get => _sources[0];
        set => _sources[0] = value;
    }

    public Task<IBitmapSource>? CurrentSourceTask {
        get => _sources[1];
        set {
            if (_sources[1] == value)
                return;

            _sources[1]?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully) {
                    r.Result.LayoutChanged -= BitmapSourceCurrentOnLayoutChanged;
                    BitmapSourceCurrentOnLayoutChanged();
                }
            });
            _sources[1] = value;
            _sources[1]?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.LayoutChanged += BitmapSourceCurrentOnLayoutChanged;
            });
        }
    }


    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceLoadAttemptFinished;

    public event Action<Task<IBitmapSource>>? AllBitmapSourceSliceLoadAttemptFinished;

    private MultiBitmapViewerControl Control { get; }

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

    public bool UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current) {
        LastException = null;
        if (PreviousSourceTask == previous && CurrentSourceTask == current)
            return false;

        PreviousSourceTask = previous;
        CurrentSourceTask = current;

        return true;
    }

    private void BitmapSourceCurrentOnLayoutChanged() {
        if (CurrentSourceTask is not { } bitmapSourceTask)
            return;

        var bitmapSource = bitmapSourceTask.Result;
        var layout = bitmapSource.Layout;

        if (layout.Any()) {
            _ = bitmapSource.GetGdipBitmapAsync(layout[0])
                .ContinueWith(_ => {
                    if (bitmapSourceTask == CurrentSourceTask || layout == bitmapSource.Layout)
                        AnyBitmapSourceSliceLoadAttemptFinished?.Invoke(bitmapSourceTask);
                });
        }

        _ = Task.WhenAll(layout.Select(bitmapSource.GetWicBitmapSourceAsync))
            .ContinueWith(_ => {
                if (bitmapSourceTask == CurrentSourceTask || layout == bitmapSource.Layout)
                    AllBitmapSourceSliceLoadAttemptFinished?.Invoke(bitmapSourceTask);
            });
    }

    public bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask)
        => bitmapSourceTask?.IsCompletedSuccessfully is true &&
            bitmapSourceTask.Result.Layout.Any(bitmapSourceTask.Result.HasGdipBitmap) &&
            (bitmapSourceTask == CurrentSourceTask || bitmapSourceTask == PreviousSourceTask);

    public bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask)
        => bitmapSourceTask?.IsCompletedSuccessfully is true &&
            bitmapSourceTask.Result.Layout.All(bitmapSourceTask.Result.HasGdipBitmap) &&
            (bitmapSourceTask == CurrentSourceTask || bitmapSourceTask == PreviousSourceTask);

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

            if (IsAnyVisibleSliceReadyForDrawing(CurrentSourceTask))
                g.Clear(Control.BackColorWhenLoaded);
            else if (CurrentSourceTask?.IsFaulted is true)
                g.Clear(Control.BackColor);
            else if (IsAnyVisibleSliceReadyForDrawing(PreviousSourceTask))
                g.Clear(Control.BackColorWhenLoaded);
            else
                g.Clear(Control.BackColor);

            var currentSourceFullyAvailable = IsEveryVisibleSliceReadyForDrawing(CurrentSourceTask);
            var isLoading = false;
            foreach (var sourceTask in _sources) {
                if (sourceTask is null)
                    continue;
                if (currentSourceFullyAvailable && PreviousSourceTask == sourceTask) {
                    Debug.Assert(PreviousSourceTask != CurrentSourceTask);
                    continue;
                }

                if (sourceTask.IsCompletedSuccessfully) {
                    var source = sourceTask.Result;
                    var imageRect = Control.EffectiveRect;
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
                        var rc = source.Layout.RectOf(sliceCell, imageRect);
                        if (bitmapTask.IsCompletedSuccessfully) {
                            g.DrawImage(bitmapTask.Result, rc);
                            continue;
                        }

                        string msg;
                        if (bitmapTask.IsFaulted) {
                            msg = $"Error\n({sliceCell.ImageIndex}, {sliceCell.Mipmap}, {sliceCell.Slice})\n" +
                                $"{bitmapTask.Exception}";
                            using var bb = new SolidBrush(Control.BackColor.MultiplyOpacity(
                                Control.OverlayBackgroundOpacity));
                            g.FillRectangle(bb, rc);
                        } else if (Control.IsLoadingBoxDelayed)
                            continue;
                        else
                            msg = "Loading...";

                        DrawText(
                            g,
                            msg,
                            rc,
                            StringAlignment.Center,
                            StringAlignment.Center,
                            Control.Font,
                            Control.ForeColor,
                            Control.BackColor,
                            1f,
                            2);
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
        RectangleF rectangle,
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
