using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private readonly ITexRenderer[] _renderers;

    public readonly PanZoomTracker Viewport;

    private int _currentDepth;
    private int _currentMipmap;
    private Color _borderColor = Color.LightGray;
    private int _transparencyCellSize = 8;

    public TexFileViewerControl() {
        MouseActivity.UseLeftDrag = MouseActivity.UseMiddleDrag = MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;

        Viewport = new(MouseActivity);
        Viewport.ViewportChanged += Invalidate;

        _renderers = new ITexRenderer[] {new D2DRenderer(this), new GraphicsRenderer(this)};
    }

    public Color BorderColor {
        get => _borderColor;
        set {
            if (_borderColor == value)
                return;
            _borderColor = value;
            foreach (var r in _renderers)
                r.BorderColor = value;
            Invalidate();
        }
    }

    public int TransparencyCellSize {
        get => _transparencyCellSize;
        set {
            if (_transparencyCellSize == value)
                return;
            _transparencyCellSize = value;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            foreach (var r in _renderers)
                r.Dispose();
            Viewport.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (FileResourceTyped is not { } fr)
            return;

        foreach (var r in _renderers) {
            if (r.LastException is not null)
                continue;

            if (!r.HasImage) {
                if (!r.LoadTexFile(fr, _currentMipmap, _currentDepth))
                    continue;
            }

            if (r.Draw(e))
                return;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        // intentionally left empty
    }

    protected override void OnForeColorChanged(EventArgs e) {
        base.OnForeColorChanged(e);
        foreach (var r in _renderers)
            r.ForeColor = ForeColor;
    }

    protected override void OnBackColorChanged(EventArgs e) {
        base.OnBackColorChanged(e);
        foreach (var r in _renderers)
            r.BackColor = BackColor;
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        Viewport.Size.IsEmpty ? base.GetPreferredSize(proposedSize) : Viewport.Size;

    public void UpdateBitmap(int depth, int mipmap, bool force = false) {
        if (FileResourceTyped is not { } frt || (_currentDepth == depth && _currentMipmap == mipmap))
            return;
        _currentDepth = _currentMipmap = 0;

        foreach (var r in _renderers)
            r.Reset();

        Viewport.Reset(new(
            frt.TextureBuffer.WidthOfMipmap(mipmap),
            frt.TextureBuffer.HeightOfMipmap(mipmap)));
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        UpdateBitmap(0, 0);
    }

    public override void ClearFile() {
        ClearFileImpl();
        base.ClearFile();
    }

    private void ClearFileImpl() {
        _currentDepth = _currentMipmap = -1;
        foreach (var r in _renderers)
            r.Reset();
        Viewport.Reset(new());
    }

    private interface ITexRenderer : IDisposable {
        bool HasImage { get; }
        Exception? LastException { get; }
        Size Size { get; }
        Color ForeColor { get; set; }
        Color BackColor { get; set; }
        Color BorderColor { get; set; }

        void Reset();
        bool LoadTexFile(TexFile texFile, int mipIndex, int depth);
        bool Draw(PaintEventArgs e);
    }
}
