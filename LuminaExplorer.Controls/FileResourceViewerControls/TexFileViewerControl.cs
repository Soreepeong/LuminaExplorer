using System.Drawing.Imaging;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private readonly BufferedGraphicsContext _context = new();
    
    public readonly PanZoomTracker Viewport;

    private TextureBuffer? _textureBuffer;
    private Bitmap? _bitmap;
    private int _currentDepth;
    private int _currentMipmap;

    public TexFileViewerControl() {
        MouseActivity.UseLeftDrag = MouseActivity.UseMiddleDrag = MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;
        
        Viewport = new(MouseActivity);
        Viewport.ViewportChanged += Invalidate;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _context.Dispose();
            Viewport.Dispose();
        }
        
        base.Dispose(disposing);
    }
    
    public bool IsWholeBitmapInViewport => ClientRectangle.Contains(Viewport.EffectiveRect);

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (_bitmap is not { } bitmap)
            return;

        var imageRect = Viewport.EffectiveRect;
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

        var zoomText = $"Zoom {Viewport.EffectiveZoom * 100:0.00}%";
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
        // intentionally left empty
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
        
        Viewport.Reset(_bitmap.Size);
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
        Viewport.Reset(new());
    }
}
