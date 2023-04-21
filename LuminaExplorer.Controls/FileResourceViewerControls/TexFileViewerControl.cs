using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using Timer = System.Windows.Forms.Timer;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private const int FadeOutDuration = 200;
    private const int FadeOutDelay = 500;

    public readonly PanZoomTracker Viewport;

    private readonly Timer _timer;

    private ITexRenderer[]? _renderers;

    private int _currentSlice;
    private int _currentMipmap;
    private Color _borderColor = Color.LightGray;
    private int _transparencyCellSize = 8;

    private long _showDescriptionAtLeastUntilMilliseconds;
    private bool _mouseInDescriptionArea;

    public TexFileViewerControl() {
        MouseActivity.UseLeftDrag = true;
        MouseActivity.UseMiddleDrag = true;
        MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;
        MouseActivity.UseInfiniteLeftDrag = true;
        MouseActivity.UseInfiniteRightDrag = true;
        MouseActivity.UseInfiniteMiddleDrag = true;

        Viewport = new(MouseActivity);
        Viewport.ViewportChanged += () => {
            ShowDescriptionForFromNow(FadeOutDelay);
            Invalidate();
        };

        _timer = new();
        _timer.Enabled = false;
        _timer.Interval = 1;
        _timer.Tick += (_, _) => {
            var remaining = _showDescriptionAtLeastUntilMilliseconds - Environment.TickCount64;
            if (remaining > FadeOutDuration) {
                _timer.Interval = (int) (remaining - FadeOutDuration);
                return;
            }
            
            _timer.Interval = 1;
            Invalidate();
            if (remaining < 0)
                _timer.Enabled = false;
        };
    }

    public Color BorderColor {
        get => _borderColor;
        set {
            if (_borderColor == value)
                return;
            _borderColor = value;
            foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
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
            foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
                r.Dispose();
            Viewport.Dispose();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (FileResourceTyped is { } fr) {
            var d = _showDescriptionAtLeastUntilMilliseconds - Environment.TickCount64;
            if (MouseActivity.IsDragging)
                ShowDescriptionForFromNow(FadeOutDelay);
            var opacity = _mouseInDescriptionArea ? 1f :
                d <= 0 ? 0f :
                d >= FadeOutDuration ? 1f : (float) d / FadeOutDuration;

            foreach (var r in _renderers ?? Array.Empty<ITexRenderer>()) {
                if (r.LastException is not null)
                    continue;

                if (!r.HasImage) {
                    if (!r.LoadTexFile(fr, _currentMipmap, _currentSlice))
                        continue;
                }

                r.DescriptionOpacity = opacity;

                if (r.Draw(e))
                    return;
            }
        }
        
        base.OnPaintBackground(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (e.Y < 80 && (e.X < Width / 2 || e.X < 160)) {
            if (!_mouseInDescriptionArea) {
                _mouseInDescriptionArea = true;
                Invalidate();
            }
        } else if (_mouseInDescriptionArea) {
            _mouseInDescriptionArea = false;
            ShowDescriptionForFromNow(FadeOutDelay);
        }
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_mouseInDescriptionArea) {
            _mouseInDescriptionArea = false;
            ShowDescriptionForFromNow(FadeOutDelay);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        // intentionally left empty
    }

    protected override void OnForeColorChanged(EventArgs e) {
        base.OnForeColorChanged(e);
        foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
            r.ForeColor = ForeColor;
    }

    protected override void OnBackColorChanged(EventArgs e) {
        base.OnBackColorChanged(e);
        foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
            r.BackColor = BackColor;
    }

    protected override void OnMarginChanged(EventArgs e) {
        base.OnMarginChanged(e);
        Invalidate();
    }

    protected override void OnPaddingChanged(EventArgs e) {
        base.OnPaddingChanged(e);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        Viewport.Size.IsEmpty
            ? base.GetPreferredSize(proposedSize)
            : new(
                Viewport.Size.Width + Margin.Horizontal,
                Viewport.Size.Height + Margin.Vertical);

    public void UpdateBitmap(int slice, int mipmap, bool force = false) {
        if (FileResourceTyped is not { } frt || (!force && _currentSlice == slice && _currentMipmap == mipmap))
            return;

        _currentSlice = slice;
        _currentMipmap = mipmap;

        foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
            r.Reset();

        Viewport.Reset(new(
            frt.TextureBuffer.WidthOfMipmap(mipmap),
            frt.TextureBuffer.HeightOfMipmap(mipmap)));
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        UpdateBitmap(0, 0);

        _renderers ??= new ITexRenderer[] {new D2DRenderer(this), new GraphicsRenderer(this)};
    }

    public override Task SetFileAsync(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        return Task.Run(async () => {
            await base.SetFileAsync(tree, file, fileResource);
            // TODO: actually load async
            await Task.Factory.StartNew(() => {
                ClearFileImpl();
                UpdateBitmap(0, 0);
                _renderers ??= new ITexRenderer[] {new D2DRenderer(this), new GraphicsRenderer(this)};
            }, default, TaskCreationOptions.None, MainTaskScheduler);
        });
    }

    public override void ClearFile() {
        ClearFileImpl();
        base.ClearFile();
    }

    public override Task ClearFileAsync() {
        // TODO: asyncize
        ClearFileImpl();
        return base.ClearFileAsync();
    }

    public void ShowDescriptionForFromNow(long durationMilliseconds) {
        if (durationMilliseconds <= 0) {
            _showDescriptionAtLeastUntilMilliseconds = 0;
            _timer.Enabled = false;
            return;
        }

        _showDescriptionAtLeastUntilMilliseconds = Math.Max(
            _showDescriptionAtLeastUntilMilliseconds,
            Environment.TickCount64 + durationMilliseconds);
        _timer.Enabled = true;
        _timer.Interval = 1;
        Invalidate();
    }

    private void ClearFileImpl() {
        _currentSlice = _currentMipmap = -1;
        foreach (var r in _renderers ?? Array.Empty<ITexRenderer>())
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
        float DescriptionOpacity { get; set; }

        void Reset();
        bool LoadTexFile(TexFile texFile, int mipIndex, int slice);
        bool Draw(PaintEventArgs e);
    }
}
