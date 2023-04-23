using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;

internal sealed class GdipTexRenderer : ITexRenderer {
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();
    private Task<IBitmapSource>? _bitmapSourcePrevious;
    private Task<IBitmapSource>? _bitmapSourceCurrent;

    public GdipTexRenderer(TexFileViewerControl control) {
        Control = control;
    }

    public void UiThreadInitialize() { }

    public void Dispose() {
        UpdateBitmapSource(null, null);
        _bufferedGraphicsContext.Dispose();
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;

    private TexFileViewerControl Control { get; }

    public Exception? LastException { get; private set; }

    private LoadState TryGetActiveSource(out IBitmapSource source, out Exception? exception) {
        source = null!;
        exception = null;
        var hasLoading = false;

        foreach (var bitmapSourceTask in new[] {_bitmapSourceCurrent, _bitmapSourcePrevious}) {
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

    public void UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current) {
        LastException = null;

        if (previous == current)
            previous = null;

        _bitmapSourcePrevious = previous;

        if (_bitmapSourceCurrent != current) {
            _bitmapSourceCurrent?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully) {
                    r.Result.ImageOrMipmapChanged -= BitmapSourceCurrentOnImageOrMipmapChanged;
                    BitmapSourceCurrentOnImageOrMipmapChanged();
                }
            });
            _bitmapSourceCurrent = current;
            _bitmapSourceCurrent?.ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.ImageOrMipmapChanged += BitmapSourceCurrentOnImageOrMipmapChanged;
            });
        }
    }

    private void BitmapSourceCurrentOnImageOrMipmapChanged() {
        if (_bitmapSourceCurrent is not { } b)
            return;
        b.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully)
                return;
            r.Result.GetGdipBitmapAsync(0).ContinueWith(_ => { AnyBitmapSourceSliceAvailableForDrawing?.Invoke(b); });
        });
    }

    public bool HasBitmapSourceReadyForDrawing(Task<IBitmapSource> bitmapSourceTask)
        => bitmapSourceTask.IsCompletedSuccessfully &&
           Enumerable.Range(0, bitmapSourceTask.Result.Depth).All(bitmapSourceTask.Result.HasGdipBitmap) &&
           (bitmapSourceTask == _bitmapSourceCurrent || bitmapSourceTask == _bitmapSourcePrevious);

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

            var loadState = TryGetActiveSource(out var source, out var exception);
            switch (loadState) {
                case LoadState.Loaded: {
                    using (var backBrush = new SolidBrush(Control.BackColorWhenLoaded))
                        g.FillRectangle(backBrush, new(new(), Control.ClientSize));
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
                    for (var i = 0; i < source.Depth; i++) {
                        var cellRect = source.Layout.RectOf(i, imageRect);

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
                        for (var i = 0; i < source.Depth; i++) {
                            g.DrawRectangle(borderPen, RectangleF.Inflate(
                                source.Layout.RectOf(i, imageRect),
                                contentBorderWidth / 2f,
                                contentBorderWidth / 2f));
                        }
                    }

                    // 3. Draw bitmaps
                    for (var i = 0; i < source.Depth; i++) {
                        var bitmapTask = source.GetGdipBitmapAsync(i);
                        if (bitmapTask.IsCompletedSuccessfully) {
                            g.DrawImage(bitmapTask.Result, source.Layout.RectOf(i, imageRect));
                        } else if (!bitmapTask.IsFaulted) {
                            if (_bitmapSourcePrevious?.IsCompletedSuccessfully is true &&
                                _bitmapSourcePrevious.Result != source) {
                                var bitmapTask2 = _bitmapSourcePrevious.Result.GetGdipBitmapAsync(i);
                                if (bitmapTask2.IsCompletedSuccessfully)
                                    g.DrawImage(bitmapTask2.Result, source.Layout.RectOf(i, imageRect));
                            }
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
                    break;
                }

                case LoadState.Loading:
                case LoadState.Error:
                case LoadState.Empty:
                default: {
                    using (var backBrush = new SolidBrush(Control.BackColor))
                        g.FillRectangle(backBrush, new(new(), Control.ClientSize));

                    if (hideIfNotLoading && loadState != LoadState.Loading)
                        overlayString = null;

                    if (overlayString is null && exception is not null)
                        overlayString = $"Error occurred loading the file.\n{exception}";
                    break;
                }
            }

            if (hideIfNotLoading && loadState != LoadState.Loading)
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
}
