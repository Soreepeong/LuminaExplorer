using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed class GdipRenderer : ITexRenderer {
        private Bitmap? _bitmap;

        private CancellationTokenSource? _loadCancellationTokenSource;

        public GdipRenderer(TexFileViewerControl control) {
            Control = control;
        }

        public void Dispose() => SafeDispose.D(ref _bitmap);

        private TexFileViewerControl Control { get; }

        public bool HasNondisposedBitmap => _bitmap is not null;

        public Size ImageSize => _bitmap?.Size ?? Size.Empty;

        public ITexRenderer.LoadState State { get; private set; } = ITexRenderer.LoadState.Empty;

        public Exception? LastException { get; private set; }

        public Task LoadTexFileAsync(TexFile texFile, int mipIndex, int slice) {
            // Currently in UI thread
            Reset(false);
            State = ITexRenderer.LoadState.Loading;

            var cts = _loadCancellationTokenSource = new();

            return Control.RunOnUiThreadAfter(Task.Run(() => {
                // Currently NOT in UI thread
                var texBuf = texFile.TextureBuffer.Filter(mipIndex, slice, TexFile.TextureFormat.B8G8R8A8);
                var width = texBuf.Width;
                var height = texBuf.Height;

                unsafe {
                    fixed (void* p = texBuf.RawData) {
                        using var b = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppArgb, (nint) p);
                        return new Bitmap(b);
                    }
                }
            }, cts.Token), r => {
                // Back in UI thread
                try {
                    cts.Token.ThrowIfCancellationRequested();

                    SafeDispose.D(ref _bitmap);

                    if (r.IsCompletedSuccessfully) {
                        _bitmap = r.Result;
                        State = ITexRenderer.LoadState.Loaded;
                    } else {
                        LastException = r.Exception ?? new Exception("This exception should not happen");
                        State = ITexRenderer.LoadState.Error;

                        throw LastException;
                    }
                } catch (Exception) {
                    if (r.IsCompletedSuccessfully)
                        r.Result.Dispose();
                    throw;
                } finally {
                    if (cts == _loadCancellationTokenSource)
                        _loadCancellationTokenSource = null;
                    cts.Dispose();
                }
            });
        }

        public void Reset(bool disposeBitmap = true) {
            LastException = null;

            if (disposeBitmap)
                SafeDispose.D(ref _bitmap);

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;

            State = ITexRenderer.LoadState.Empty;
        }

        public bool Draw(PaintEventArgs e) {
            try {
                var g = e.Graphics;

                if (_bitmap is not { } bitmap) {
                    using var backBrush = new SolidBrush(Control.BackColor);
                    g.FillRectangle(backBrush, new(new(), Control.ClientSize));
                    return true;
                }

                using (var backBrush = new SolidBrush(Control.BackColorWhenLoaded))
                    g.FillRectangle(backBrush, new(new(), Control.ClientSize));

                var imageRect = Control.Viewport.EffectiveRect;
                var clientSize = Control.ClientSize;
                var overlayRect = new Rectangle(
                    Control.Padding.Left + Control.Margin.Left,
                    Control.Padding.Top + Control.Margin.Top,
                    clientSize.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
                    clientSize.Height - Control.Padding.Vertical - Control.Margin.Vertical);

                if (Control.ShouldDrawTransparencyGrid(e.ClipRectangle,
                        out var multiplier,
                        out var minX,
                        out var minY,
                        out var dx,
                        out var dy)) {
                    using var cellBrush1 = new SolidBrush(Control.TransparencyCellColor1);
                    using var cellBrush2 = new SolidBrush(Control.TransparencyCellColor2);
                    var rc = new Rectangle(0, minY * multiplier + dy, 0, multiplier);
                    var yLim = Math.Min(imageRect.Bottom, e.ClipRectangle.Bottom);
                    var xLim = Math.Min(imageRect.Right, e.ClipRectangle.Right);
                    for (var y = minY;; y++) {
                        rc.Height = Math.Min(multiplier, yLim - rc.Y);
                        if (rc.Height <= 0)
                            break;

                        rc.X = minX * multiplier + dx;
                        rc.Width = multiplier;
                        for (var x = minX;; x++) {
                            rc.Width = Math.Min(multiplier, xLim - rc.X);
                            if (rc.Width <= 0)
                                break;

                            g.FillRectangle(
                                (x + y) % 2 == 0 ? cellBrush1 : cellBrush2,
                                x * multiplier + dx,
                                y * multiplier + dy,
                                multiplier,
                                multiplier);

                            rc.X += multiplier;
                        }

                        rc.Y += multiplier;
                    }
                }

                g.InterpolationMode = Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.Bilinear;
                g.DrawImage(bitmap, imageRect);

                if (Control.ContentBorderWidth > 0) {
                    using var pen = new Pen(Control.ContentBorderColor, Control.ContentBorderWidth);
                    g.DrawRectangle(pen, Rectangle.Inflate(imageRect, 1, 1));
                }

                if (State == ITexRenderer.LoadState.Loading) {
                    using var backBrush = new SolidBrush(
                        Control.BackColor.MultiplyOpacity(Control.LoadingBackgroundOverlayOpacity));
                    g.FillRectangle(backBrush, new(new(), Control.ClientSize));
                    
                    DrawText(
                        g,
                        Control.LoadingText,
                        overlayRect,
                        StringAlignment.Center,
                        StringAlignment.Center,
                        Control.Font,
                        Control.ForeColor,
                        Control.BackColor,
                        1f,
                        2);
                } else {
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
                }

                return true;
            } catch (Exception ex) {
                State = ITexRenderer.LoadState.Error;
                LastException = ex;
                return false;
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
}