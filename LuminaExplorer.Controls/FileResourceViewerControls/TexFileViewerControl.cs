using System.Drawing.Imaging;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private readonly BufferedGraphicsContext _context = new();

    private TextureBuffer? _textureBuffer;
    private Bitmap? _bitmap;
    private int _currentDepth;
    private int _currentMipmap;
    private int? _zoomFactor;
    private Point _bitmapOffset;

    private bool _mouseDragging;
    private Point _mouseDragOrigin;

    protected override void Dispose(bool disposing) {
        if (disposing)
            _context.Dispose();
    }

    public int? ZoomFactor {
        get => _zoomFactor;
        set {
            if (_zoomFactor == value)
                return;

            _zoomFactor = value;
            UpdateBitmapOffset(BitmapOffset);
            Invalidate();
        }
    }

    public float EffectiveZoomFactor => MathF.Log2(EffectiveZoom) * 8 * SystemInformation.MouseWheelScrollDelta;

    public float EffectiveZoom {
        get {
            if (_bitmap is not { } bitmap)
                return 1f;
            if (_zoomFactor is { } zoomFactor)
                return MathF.Pow(2, zoomFactor / 8f / SystemInformation.MouseWheelScrollDelta);
            if (bitmap.Width <= Width && bitmap.Height < Height)
                return 1f;
            if (Width * bitmap.Height > bitmap.Width * Height)
                return 1f * Height / bitmap.Height;
            return 1f * Width / bitmap.Width;
        }
    }

    public Point BitmapOffset {
        get => _bitmapOffset;
        set => UpdateBitmapOffset(value);
    }

    public Rectangle ImageRect {
        get {
            if (_bitmap is null)
                return Rectangle.Empty;

            var targetWidth = (int) Math.Round(_bitmap.Width * EffectiveZoom);
            var targetHeight = (int) Math.Round(_bitmap.Height * EffectiveZoom);
            return new(
                (Width - targetWidth) / 2 + _bitmapOffset.X,
                (Height - targetHeight) / 2 + _bitmapOffset.Y,
                targetWidth,
                targetHeight);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);

        if (Screen.PrimaryScreen is not { } primaryScreen)
            return;
        if (e.Button == MouseButtons.Left && !_mouseDragging) {
            Capture = _mouseDragging = true;
            _mouseDragOrigin = Cursor.Position;
            Cursor.Hide();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!_mouseDragging)
            return;
        var dx = Cursor.Position.X - _mouseDragOrigin.X;
        var dy = Cursor.Position.Y - _mouseDragOrigin.Y;
        BitmapOffset = new(_bitmapOffset.X + dx, _bitmapOffset.Y + dy);
        Cursor.Position = _mouseDragOrigin;
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if (!_mouseDragging)
            return;
        Capture = _mouseDragging = false;
        Cursor.Position = _mouseDragOrigin;
        Cursor.Show();
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        var wheelDelta = SystemInformation.MouseWheelScrollDelta;
        var zoomRange = 64 * wheelDelta;
        if (_zoomFactor is { } zoomFactor) {
            UpdateZoomFactor(Math.Clamp(e.Delta + zoomFactor, -zoomRange, zoomRange), new(e.X, e.Y));
        } else if (e.Delta != 0) {
            zoomFactor = e.Delta switch {
                > 0 => (int) Math.Floor(EffectiveZoomFactor / wheelDelta) * wheelDelta,
                < 0 => (int) Math.Ceiling(EffectiveZoomFactor / wheelDelta) * wheelDelta,
                _ => throw new FailFastException("e.Delta must be not 0 at this point")
            };
            UpdateZoomFactor(Math.Clamp(e.Delta + zoomFactor, -zoomRange, zoomRange), new(e.X, e.Y));
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (_bitmap is not { } bitmap)
            return;

        var imageRect = ImageRect;
        var insetRect = new Rectangle(
            Padding.Left,
            Padding.Top,
            Width - Padding.Left - Padding.Right,
            Height - Padding.Bottom - Padding.Top);

        using var buffer = _context.Allocate(e.Graphics, e.ClipRectangle);
        var g = buffer.Graphics;

        using var backBrush = new SolidBrush(BackColor);
        using var foreBrush = new SolidBrush(ForeColor);

        g.FillRectangle(backBrush, e.ClipRectangle);
        g.DrawImage(bitmap, imageRect);
        using (var borderPen = new Pen(Color.LightGray)) {
            g.DrawRectangle(
                borderPen,
                imageRect.Left - 1,
                imageRect.Top - 1,
                imageRect.Width + 1,
                imageRect.Height + 1);
        }

        var stringFormat = new StringFormat {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Far,
            Trimming = StringTrimming.None,
        };

        var zoomText = $"Zoom {EffectiveZoom * 100:0.00}%";
        for (var i = -2; i <= 2; i++) {
            for (var j = -2; j <= 2; j++) {
                if (i == 0 && j == 0)
                    continue;
                g.DrawString(
                    zoomText,
                    Font,
                    backBrush,
                    insetRect with {Width = insetRect.Width + i, Height = insetRect.Height + j},
                    stringFormat);
            }
        }

        g.DrawString(
            zoomText,
            Font,
            foreBrush,
            insetRect,
            stringFormat);

        buffer.Render();
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        // empty
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        UpdateBitmapOffset(BitmapOffset);
        Invalidate();
    }

    public void UpdateBitmap(int depth, int mipmap) {
        if (_textureBuffer is null || (_currentDepth == depth && _currentMipmap == mipmap))
            return;
        _currentDepth = _currentMipmap = 0;

        _bitmap?.Dispose();

        var width = _textureBuffer.WidthOfMipmap(mipmap);
        var height = _textureBuffer.HeightOfMipmap(mipmap);
        var mipmapOffset = _textureBuffer.MipmapAllocations.Take(mipmap).Sum();
        unsafe {
            fixed (void* p = _textureBuffer.RawData) {
                using var b = new Bitmap(width, height, 4 * width, PixelFormat.Format32bppArgb,
                    (nint) p + mipmapOffset + 4 * width * height * depth);
                _bitmap = new(b);
            }
        }

        Invalidate();
    }

    public void UpdateZoomFactor(int newZoomFactor, Point cursor) {
        var old = new Point(
            (int)((cursor.X - Width / 2f - _bitmapOffset.X) / EffectiveZoom),
            (int)((cursor.Y - Height / 2f - _bitmapOffset.Y) / EffectiveZoom));

        ZoomFactor = newZoomFactor;

        BitmapOffset = new(
            (int) (cursor.X - Width / 2f - old.X * EffectiveZoom),
            (int) (cursor.Y - Height / 2f - old.Y * EffectiveZoom));
    }

    private void UpdateBitmapOffset(Point value) {
        if (_bitmap is null) {
            value = new(0, 0);
        } else {
            var targetWidth = (int) Math.Round(_bitmap.Width * EffectiveZoom);
            var xrange = targetWidth / 2;
            value.X = targetWidth <= Width ? 0 : Math.Clamp(value.X, -xrange, xrange);

            var targetHeight = (int) Math.Round(_bitmap.Height * EffectiveZoom);
            var yrange = targetHeight / 2;
            value.Y = targetHeight <= Height ? 0 : Math.Clamp(value.Y, -yrange, yrange);
        }

        if (value == _bitmapOffset)
            return;
        _bitmapOffset = value;
        Invalidate();
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, TexFile fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        _textureBuffer = fileResource.TextureBuffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
        UpdateBitmap(0, 0);
    }

    public override void ClearFile() {
        ClearFileImpl();
        base.ClearFile();
    }

    private void ClearFileImpl() {
        _bitmap?.Dispose();
        _bitmap = null;
        _textureBuffer = null;
        _currentDepth = _currentMipmap = -1;
        _bitmapOffset = new();
        _zoomFactor = null;
    }
}
