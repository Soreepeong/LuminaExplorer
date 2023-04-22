using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed class GdipRenderer : ITexRenderer {
        private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();
        private Bitmap?[] _bitmaps = Array.Empty<Bitmap?>();
        private IGridLayout? _layout;

        private CancellationTokenSource? _loadCancellationTokenSource;

        public GdipRenderer(TexFileViewerControl control) {
            Control = control;
        }

        public void Dispose() {
            SafeDispose.Array(ref _bitmaps);
            _bufferedGraphicsContext.Dispose();
        }

        private TexFileViewerControl Control { get; }

        public bool HasNondisposedBitmap => _bitmaps.Any() && _bitmaps.All(x => x is not null);

        public Size ImageSize => _layout?.GridSize ?? Size.Empty;

        public ITexRenderer.LoadState State { get; private set; } = ITexRenderer.LoadState.Empty;

        public Exception? LastException { get; private set; }

        public Task LoadFileAsync(int mipIndex) {
            if (Control.FileResourceTyped is { } texFile)
                return LoadTexFileAsync(texFile, mipIndex);
            if (Control.PhysicalFile is { } pfile) {
                // TODO
            }

            Reset(false);
            return Task.CompletedTask;
        }

        private Task LoadTexFileAsync(TexFile texFile, int mipIndex) {
            Reset(false);
            State = ITexRenderer.LoadState.Loading;

            var cts = _loadCancellationTokenSource = new();

            return Control.RunOnUiThreadAfter(
                Task.WhenAll(Enumerable
                    .Range(0, texFile.TextureBuffer.DepthOfMipmap(mipIndex))
                    .Select(i => Task.Run(() => {
                        cts.Token.ThrowIfCancellationRequested();
                        var texBuf = texFile.TextureBuffer.Filter(mipIndex, i, TexFile.TextureFormat.B8G8R8A8);
                        var width = texBuf.Width;
                        var height = texBuf.Height;
                        unsafe {
                            fixed (void* p = texBuf.RawData) {
                                using var b = new Bitmap(
                                    width,
                                    height,
                                    4 * width,
                                    PixelFormat.Format32bppArgb,
                                    (nint) p);
                                return new Bitmap(b);
                            }
                        }
                    }, cts.Token))),
                r => {
                    try {
                        cts.Token.ThrowIfCancellationRequested();

                        SafeDispose.Array(ref _bitmaps);
                        _layout = null;

                        if (r.IsCompletedSuccessfully) {
                            _bitmaps = r.Result;
                            _layout = Control.CreateGridLayout(mipIndex);
                            State = ITexRenderer.LoadState.Loaded;
                        } else {
                            LastException = r.Exception ?? new Exception("This exception should not happen");
                            State = ITexRenderer.LoadState.Error;

                            throw LastException;
                        }
                    } catch (Exception) {
                        if (r.IsCompletedSuccessfully)
                            SafeDispose.Enumerable(r.Result);
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

            if (disposeBitmap) {
                SafeDispose.Array(ref _bitmaps);
                _layout = null;
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;

            State = ITexRenderer.LoadState.Empty;
        }

        public bool Draw(PaintEventArgs e) {
            BufferedGraphics? bufferedGraphics = null;
            try {
                bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
                var g = bufferedGraphics.Graphics;

                if (_layout is not { } layout) {
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

                g.InterpolationMode = Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.Bilinear;

                // 1. Draw transparency grids
                for (var i = 0; i < _bitmaps.Length; i++) {
                    var cellRect = layout.RectOf(i, imageRect);

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
                var contentBorderWidth = Control._contentBorderWidth;
                if (contentBorderWidth > 0) {
                    using var borderPen = new Pen(Control.ContentBorderColor, Control.ContentBorderWidth);
                    for (var i = 0; i < _bitmaps.Length; i++) {
                        g.DrawRectangle(borderPen, RectangleF.Inflate(
                            layout.RectOf(i, imageRect),
                            contentBorderWidth / 2f,
                            contentBorderWidth / 2f));
                    }
                }

                // 3. Draw bitmaps
                for (var i = 0; i < _bitmaps.Length; i++) {
                    var bitmap = _bitmaps[i];
                    if (bitmap is null) {
                        Debug.Print("_bitmaps contains null when _layout is cell?");
                        continue;
                    }

                    g.DrawImage(bitmap, layout.RectOf(i, imageRect));
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

                var overlayString = Control.OverlayString;
                var overlayOpacity = Control.OverlayOpacity;
                if (string.IsNullOrWhiteSpace(overlayString) || overlayOpacity == 0) {
                    if (State is ITexRenderer.LoadState.Loading or ITexRenderer.LoadState.Empty) {
                        if (!Control.IsLoadingBoxDelayed) {
                            overlayString = Control.LoadingText;
                            overlayOpacity = Control.OverlayBackgroundOpacity;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(overlayString) && overlayOpacity > 0) {
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

                    using var backBrush = new SolidBrush(Control.BackColor.MultiplyOpacity(overlayOpacity));
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
                        overlayOpacity,
                        2);
                }

                return true;
            } catch (Exception ex) {
                State = ITexRenderer.LoadState.Error;
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
}
