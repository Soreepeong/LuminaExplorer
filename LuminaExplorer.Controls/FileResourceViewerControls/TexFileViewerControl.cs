using System.Drawing.Imaging;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    public const int ZoomFactorUnit = 1 << 8;

    private readonly BufferedGraphicsContext _context = new();

    private TextureBuffer? _textureBuffer;
    private Bitmap? _bitmap;
    private int _currentDepth;
    private int _currentMipmap;
    private int? _zoomFactor;
    private Point _bitmapOffset;
    private int _zoomFactorRange = 1 << 16;

    public TexFileViewerControl() {
        MouseActivity.UseLeftDrag = MouseActivity.UseMiddleDrag = MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
    }

    protected override void Dispose(bool disposing) {
        if (disposing)
            _context.Dispose();
    }

    public int ZoomFactorWheelUnit { get; set; } = 1 << 5;

    public int ZoomFactorDragUnit { get; set; } = 1 << 0;

    public int ZoomFactorRange {
        get => _zoomFactorRange;
        set {
            _zoomFactorRange = value;
            if (_zoomFactor is { } zoomFactor)
                ZoomFactor = Math.Clamp(zoomFactor, -value, value);
        }
    }

    public int? ZoomFactor {
        get => _zoomFactor;
        set => UpdateZoomFactor(value, new(Width / 2, Height / 2));
    }

    public float EffectiveZoomFactor => MathF.Log2(EffectiveZoom) * ZoomFactorUnit;

    public float? Zoom {
        get => _zoomFactor is null ? null : EffectiveZoom;
        set {
            if (value is { } v)
                ZoomFactor = (int) Math.Round(MathF.Log2(v) * ZoomFactorUnit);
            else
                ZoomFactor = null;
        }
    }

    public float EffectiveZoom {
        get {
            if (_zoomFactor is { } zoomFactor)
                return MathF.Pow(2, 1f * zoomFactor / ZoomFactorUnit);
            return DefaultZoom;
        }
    }

    public float DefaultZoom => _bitmap is not { } bitmap
        ? 1f
        : bitmap.Width <= Width && bitmap.Height < Height
            ? 1f
            : FillingZoom;

    public float FillingZoom => _bitmap is not { } bitmap
        ? 1f
        : Width * bitmap.Height > bitmap.Width * Height
            ? 1f * Height / bitmap.Height
            : 1f * Width / bitmap.Width;

    public bool IsWholeBitmapInViewport => ClientRectangle.Contains(ImageRect);

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

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);

        if (MouseActivity.TryGetNextDragDelta(out var delta)) {
            var multiplier = 1 << (
                (MouseActivity.IsLeftHeld ? 1 : 0) +
                (MouseActivity.IsRightHeld ? 1 : 0) +
                (MouseActivity.IsMiddleHeld ? 1 : 0) - 1);
            if (MouseActivity.IsLeftDoubleDown) {
                ZoomFactor = (int) Math.Round(EffectiveZoomFactor) + (delta.X + delta.Y) *
                    ZoomFactorDragUnit *
                    multiplier;
            } else {
                BitmapOffset = new(
                    _bitmapOffset.X + delta.X * multiplier,
                    _bitmapOffset.Y + delta.Y * multiplier);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Left && MouseActivity.IsLeftDoubleUp) {
            Zoom = FillingZoom switch {
                < 1 => Zoom is null ? 1 : null,
                > 1 => EffectiveZoom * 2 < 1 + FillingZoom ? FillingZoom : null,
                _ => null,
            };
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);

        var wheelDelta = SystemInformation.MouseWheelScrollDelta;
        var normalizedDelta = (int) Math.Ceiling((float) Math.Abs(e.Delta) * ZoomFactorWheelUnit / wheelDelta);
        if (e.Delta < 0)
            normalizedDelta = -normalizedDelta;

        if (normalizedDelta == 0)
            return;

        if (_zoomFactor is not { } zoomFactor) {
            zoomFactor = normalizedDelta switch {
                > 0 => (int) Math.Floor(EffectiveZoomFactor),
                < 0 => (int) Math.Ceiling(EffectiveZoomFactor),
                _ => throw new FailFastException("e.Delta must be not 0 at this point")
            };
            UpdateZoomFactor(zoomFactor + normalizedDelta, new(e.X, e.Y));
        } else {
            var effectiveZoom = EffectiveZoom;
            var defaultZoom = DefaultZoom;
            var nextZoom = MathF.Pow(2, 1f * (zoomFactor + normalizedDelta) / ZoomFactorUnit);
            if (effectiveZoom < defaultZoom && defaultZoom <= nextZoom)
                UpdateZoomFactor(null, new(e.X, e.Y));
            else if (nextZoom <= defaultZoom && defaultZoom < effectiveZoom)
                UpdateZoomFactor(null, new(e.X, e.Y));
            else if (effectiveZoom < 1 && 1 <= nextZoom)
                UpdateZoomFactor(0, new(e.X, e.Y));
            else if (nextZoom <= 1 && 1 < effectiveZoom)
                UpdateZoomFactor(0, new(e.X, e.Y));
            else
                UpdateZoomFactor(zoomFactor + normalizedDelta, new(e.X, e.Y));
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

    public override Size GetPreferredSize(Size proposedSize) =>
        _bitmap is { } b ? b.Size : base.GetPreferredSize(proposedSize);

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

    public void UpdateZoomFactor(int? value, Point cursor) {
        if (value is not null)
            value = Math.Clamp(value.Value, -ZoomFactorRange, +ZoomFactorRange);
        if (value == _zoomFactor)
            return;

        var old = new Point(
            (int) ((cursor.X - Width / 2f - _bitmapOffset.X) / EffectiveZoom),
            (int) ((cursor.Y - Height / 2f - _bitmapOffset.Y) / EffectiveZoom));

        _zoomFactor = value;
        UpdateBitmapOffset(BitmapOffset);
        Invalidate();

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

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        _textureBuffer = FileResourceTyped!.TextureBuffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
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
