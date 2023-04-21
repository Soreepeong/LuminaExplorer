using System.Drawing.Imaging;
using Lumina.Data.Files;

namespace LuminaExplorer.Controls.FileResourceViewerControls; 

public partial class TexFileViewerControl {
    private sealed class GraphicsRenderer : ITexRenderer {
        private readonly BufferedGraphicsContext _context = new();

        private readonly TexFileViewerControl _control;

        private Bitmap? _bitmap;

        public GraphicsRenderer(TexFileViewerControl control) {
            _control = control;
        }

        public void Dispose() {
            _bitmap?.Dispose();
            _bitmap = null;
            _context.Dispose();
        }

        public bool HasImage => _bitmap is not null;
        public Exception? LastException { get; private set; }
        public Size Size { get; private set; }
        public Color ForeColor { get; set; }
        public Color BackColor { get; set; }
        public Color BorderColor { get; set; }

        public bool LoadTexFile(TexFile texFile, int mipIndex, int slice) {
            try {
                var texBuf = texFile.TextureBuffer.Filter(mipIndex, slice, TexFile.TextureFormat.B8G8R8A8);
                var width = texBuf.Width;
                var height = texBuf.Height;

                Bitmap newBitmap;
                unsafe {
                    fixed (void* p = texBuf.RawData) {
                        using var b = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppArgb, (nint) p);
                        newBitmap = new(b);
                    }
                }

                _bitmap?.Dispose();
                _bitmap = null;
                _bitmap = newBitmap;

                Size = _bitmap.Size;
                return true;
            } catch (Exception e) {
                LastException = e;
                return false;
            }
        }

        public void Reset() {
            _bitmap?.Dispose();
            _bitmap = null;
            LastException = null;
        }

        public bool Draw(PaintEventArgs e) {
            try {
                using var buffer = _context.Allocate(e.Graphics, e.ClipRectangle);
                var g = buffer.Graphics;

                using var backBrush = new SolidBrush(BackColor);

                g.FillRectangle(backBrush, e.ClipRectangle);
                var cellSize = _control.TransparencyCellSize;
                if (cellSize > 0) {
                    var controlSize = Size;
                    var c1 = false;
                    using var cellBrush = new SolidBrush(Color.LightGray);
                    for (var i = 0; i < controlSize.Width; i += cellSize) {
                        var c2 = c1;
                        c1 = !c1;
                        for (var j = 0; j < controlSize.Height; j += cellSize) {
                            if (c2)
                                g.FillRectangle(cellBrush, i, j, i + cellSize, j + cellSize);

                            c2 = !c2;
                        }
                    }
                }

                if (_bitmap is not { } bitmap) {
                    buffer.Render();
                    return true;
                }

                var imageRect = _control.Viewport.EffectiveRect;
                var insetRect = new Rectangle(
                    _control.Padding.Left,
                    _control.Padding.Top,
                    _control.Width - _control.Padding.Left - _control.Padding.Right,
                    _control.Height - _control.Padding.Bottom - _control.Padding.Top);

                g.DrawImage(bitmap, imageRect);
                using (var borderPen = new Pen(Color.LightGray))
                    g.DrawRectangle(borderPen, Rectangle.Inflate(imageRect, 1, 1));

                var stringFormat = new StringFormat {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Far,
                    Trimming = StringTrimming.None,
                };

                var zoomText = $"Zoom {_control.Viewport.EffectiveZoom * 100:0.00}%";
                for (var i = -2; i <= 2; i++) {
                    for (var j = -2; j <= 2; j++) {
                        if (i == 0 && j == 0)
                            continue;
                        g.DrawString(
                            zoomText,
                            _control.Font,
                            backBrush,
                            insetRect with {Width = insetRect.Width + i, Height = insetRect.Height + j},
                            stringFormat);
                    }
                }

                using var foreBrush = new SolidBrush(ForeColor);
                g.DrawString(
                    zoomText,
                    _control.Font,
                    foreBrush,
                    insetRect,
                    stringFormat);

                buffer.Render();
                return true;
            } catch (Exception ex) {
                LastException = ex;
                return false;
            }
        }
    }
}
