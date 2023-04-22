using System.Diagnostics.CodeAnalysis;
using System.Text;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;
using Timer = System.Windows.Forms.Timer;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private const int FadeOutDurationMs = 200;
    private readonly TimeSpan _fadeOutDelay = TimeSpan.FromSeconds(1);
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();

    public readonly PanZoomTracker Viewport;

    private Task<ITexRenderer[]>? _renderers;

    private int _currentMipmap;

    private string? _loadingFileNameWhenEmpty;
    private Color _foreColorWhenLoaded = Color.White;
    private Color _backColorWhenLoaded = Color.Black;
    private Color _contentBorderColor = Color.DarkGray;
    private int _contentBorderWidth = 1;
    private Color _transparencyCellColor1 = Color.White;
    private Color _transparencyCellColor2 = Color.LightGray;
    private int _transparencyCellSize = 8;
    private float _nearestNeighborMinimumZoom = 2f;
    private float _pixelGridMinimumZoom = 5f;
    private float _loadingBackgroundOverlayOpacity = 0.3f;
    private Size _sliceSpacing = new(16, 16);

    private readonly Timer _autoDescriptionFadeTimer;
    private long _autoDescriptionShowUntilTicks;
    private bool _autoDescriptionBeingHovered;
    private string? _autoDescriptionCached;
    private float _autoDescriptionSourceZoom = float.NaN;
    private Rectangle? _autoDescriptionRectangle;

    public TexFileViewerControl() {
        ResizeRedraw = true;

        MouseActivity.UseLeftDrag = true;
        MouseActivity.UseMiddleDrag = true;
        MouseActivity.UseRightDrag = true;
        MouseActivity.UseDoubleDetection = true;
        MouseActivity.UseWheelZoom = true;
        MouseActivity.UseDragZoom = true;
        MouseActivity.UseInfiniteLeftDrag = true;
        MouseActivity.UseInfiniteRightDrag = true;
        MouseActivity.UseInfiniteMiddleDrag = true;

        MouseActivity.Enabled = false;
        Viewport = new(MouseActivity);
        Viewport.PanExtraRange = new(_transparencyCellSize * 2);
        Viewport.ViewportChanged += () => {
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
            Invalidate();
        };

        _autoDescriptionFadeTimer = new();
        _autoDescriptionFadeTimer.Enabled = false;
        _autoDescriptionFadeTimer.Interval = 1;
        _autoDescriptionFadeTimer.Tick += (_, _) => {
            var remaining = _autoDescriptionShowUntilTicks - Environment.TickCount64;
            if (remaining > FadeOutDurationMs) {
                _autoDescriptionFadeTimer.Interval = (int) (remaining - FadeOutDurationMs);
                return;
            }

            _autoDescriptionFadeTimer.Interval = 1;
            Invalidate(AutoDescriptionRectangle);
            if (remaining < 0)
                _autoDescriptionFadeTimer.Enabled = false;
        };

        TryGetRenderers(out _, true);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _bufferedGraphicsContext.Dispose();
            if (TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    r.Dispose();
            Viewport.Dispose();
            _autoDescriptionFadeTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    public event EventHandler? ForeColorWhenLoadedChanged;

    public event EventHandler? BackColorWhenLoadedChanged;

    public event EventHandler? BorderColorChanged;

    public event EventHandler? TransparencyCellColor1Changed;

    public event EventHandler? TransparencyCellColor2Changed;

    public event EventHandler? SliceSpacingChanged;

    public event EventHandler? ImageLoadedForDisplay;

    public string? LoadingFileNameWhenEmpty {
        get => _loadingFileNameWhenEmpty;
        set {
            if (_loadingFileNameWhenEmpty == value)
                return;
            _loadingFileNameWhenEmpty = value;
            Invalidate();
        }
    }

    public Color ForeColorWhenLoaded {
        get => _foreColorWhenLoaded;
        set {
            if (_foreColorWhenLoaded == value)
                return;
            _foreColorWhenLoaded = value;
            ForeColorWhenLoadedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public Color BackColorWhenLoaded {
        get => _backColorWhenLoaded;
        set {
            if (_backColorWhenLoaded == value)
                return;
            _backColorWhenLoaded = value;
            BackColorWhenLoadedChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public Color ContentBorderColor {
        get => _contentBorderColor;
        set {
            if (_contentBorderColor == value)
                return;
            _contentBorderColor = value;
            BorderColorChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public int ContentBorderWidth {
        get => _contentBorderWidth;
        set {
            if (_contentBorderWidth == value)
                return;
            _contentBorderWidth = value;
            Invalidate();
        }
    }

    public Color TransparencyCellColor1 {
        get => _transparencyCellColor1;
        set {
            if (_transparencyCellColor1 == value)
                return;
            _transparencyCellColor1 = value;
            TransparencyCellColor1Changed?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public Color TransparencyCellColor2 {
        get => _transparencyCellColor2;
        set {
            if (_transparencyCellColor2 == value)
                return;
            _transparencyCellColor2 = value;
            TransparencyCellColor2Changed?.Invoke(this, EventArgs.Empty);
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

    public Padding PanExtraRange {
        get => Viewport.PanExtraRange;
        set => Viewport.PanExtraRange = value;
    }

    public float LoadingBackgroundOverlayOpacity {
        get => _loadingBackgroundOverlayOpacity;
        set {
            if (!Equals(_loadingBackgroundOverlayOpacity, value))
                return;
            _loadingBackgroundOverlayOpacity = value;
            Invalidate();
        }
    }

    public float NearestNeighborMinimumZoom {
        get => _nearestNeighborMinimumZoom;
        set {
            if (Equals(_nearestNeighborMinimumZoom, value))
                return;
            _nearestNeighborMinimumZoom = value;
            Invalidate();
        }
    }

    // TODO
    public float PixelGridMinimumZoom {
        get => _pixelGridMinimumZoom;
        set {
            if (Equals(_pixelGridMinimumZoom, value))
                return;
            _pixelGridMinimumZoom = value;
            Invalidate();
        }
    }

    public Size SliceSpacing {
        get => _sliceSpacing;
        set {
            if (_sliceSpacing == value)
                return;
            
            _sliceSpacing = value;
            SliceSpacingChanged?.Invoke(this, EventArgs.Empty);
            Viewport.Size = CreateGridLayout(_currentMipmap).GridSize;
            Invalidate();
            
            if (TryGetRenderers(out var renderers)) {
                foreach (var r in renderers) {
                    if (r.State is ITexRenderer.LoadState.Loading or ITexRenderer.LoadState.Loaded) {
                        Viewport.Size = Size.Add(r.ImageSize, new(value.Width, value.Height));
                        Invalidate();
                    }
                }
            }
        }
    }

    public string AutoDescription {
        get {
            var effectiveZoom = Viewport.EffectiveZoom;
            if (_autoDescriptionCached is not null && Equals(effectiveZoom, _autoDescriptionSourceZoom))
                return _autoDescriptionCached;

            if (FileResourceTyped is not { } texFile ||
                File is not { } file)
                return "";

            _autoDescriptionSourceZoom = effectiveZoom;
            var sb = new StringBuilder();
            sb.AppendLine(file.Name);
            sb.Append(texFile.Header.Format).Append("; ")
                .Append($"{texFile.Data.Length:##,###} Bytes");
            if (texFile.Header.MipLevels > 1)
                sb.Append("; ").Append(texFile.Header.MipLevels).Append(" mipmaps");
            sb.AppendLine();
            if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                sb.Append("1D: ").Append(texFile.Header.Width);
            if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                sb.Append("2D: ").Append(texFile.Header.Width)
                    .Append(" x ").Append(texFile.Header.Height);
            if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                sb.Append("3D: ").Append(texFile.Header.Width)
                    .Append(" x ").Append(texFile.Header.Height)
                    .Append(" x ").Append(texFile.Header.Depth);
            if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                sb.Append("Cube: ").Append(texFile.Header.Width)
                    .Append(" x ").Append(texFile.Header.Height);
            if (!Equals(effectiveZoom, 1f))
                sb.Append($" ({effectiveZoom * 100:0.00}%)");
            sb.AppendLine();
            foreach (var f in new[] {
                         TexFile.Attribute.DiscardPerFrame,
                         TexFile.Attribute.DiscardPerMap,
                         TexFile.Attribute.Managed,
                         TexFile.Attribute.UserManaged,
                         TexFile.Attribute.CpuRead,
                         TexFile.Attribute.LocationMain,
                         TexFile.Attribute.NoGpuRead,
                         TexFile.Attribute.AlignedSize,
                         TexFile.Attribute.EdgeCulling,
                         TexFile.Attribute.LocationOnion,
                         TexFile.Attribute.ReadWrite,
                         TexFile.Attribute.Immutable,
                         TexFile.Attribute.TextureRenderTarget,
                         TexFile.Attribute.TextureDepthStencil,
                         TexFile.Attribute.TextureSwizzle,
                         TexFile.Attribute.TextureNoTiled,
                         TexFile.Attribute.TextureNoSwizzle
                     })
                if (texFile.Header.Type.HasFlag(f))
                    sb.Append("+ ").AppendLine(f.ToString());
            return _autoDescriptionCached = sb.ToString();
        }
    }

    private float AutoDescriptionOpacity {
        get {
            var d = _autoDescriptionShowUntilTicks - Environment.TickCount64;
            return _autoDescriptionBeingHovered ? 1f :
                d <= 0 ? 0f :
                d >= FadeOutDurationMs ? 1f : (float) d / FadeOutDurationMs;
        }
    }

    private Rectangle AutoDescriptionRectangle {
        get {
            if (_autoDescriptionRectangle is not null)
                return _autoDescriptionRectangle.Value;
            var rc = new Rectangle(
                Padding.Left + Margin.Left,
                Padding.Top + Margin.Top,
                ClientSize.Width - Padding.Horizontal - Margin.Horizontal,
                ClientSize.Height - Padding.Vertical - Margin.Vertical);

            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.None,
            };

            using var g = CreateGraphics();
            var measured = g.MeasureString(
                AutoDescription,
                Font,
                new SizeF(rc.Width, rc.Height),
                stringFormat);
            rc.Width = (int) Math.Ceiling(measured.Width);
            rc.Height = (int) Math.Ceiling(measured.Height);
            _autoDescriptionRectangle = rc;
            return rc;
        }
    }

    private string LoadingText => string.IsNullOrWhiteSpace(File?.Name ?? _loadingFileNameWhenEmpty)
        ? "Loading..."
        : $"Loading {File?.Name ?? _loadingFileNameWhenEmpty}...";

    private bool ShouldDrawTransparencyGrid(
        RectangleF cellRect,
        RectangleF clipRect,
        out int multiplier,
        out int minX,
        out int minY,
        out int dx,
        out int dy) {
        multiplier = TransparencyCellSize;
        dx = dy = minX = minY = 0;

        // Is transparency grid disabled?
        if (multiplier <= 0)
            return false;

        // Is image completely out of drawing region?
        if (cellRect.Right <= clipRect.Left ||
            cellRect.Bottom <= clipRect.Top ||
            cellRect.Left >= clipRect.Right ||
            cellRect.Top >= clipRect.Bottom)
            return false;

        minX = cellRect.Left < 0
            ? (int)-MiscUtils.DivRem(cellRect.Left, -multiplier, out var fdx)
            : (int)MiscUtils.DivRem(cellRect.Left, multiplier, out fdx);
        minY = cellRect.Top < 0
            ? (int)-MiscUtils.DivRem(cellRect.Top, -multiplier, out var fdy)
            : (int)MiscUtils.DivRem(cellRect.Top, multiplier, out fdy);

        if (minX * multiplier < clipRect.Left)
            minX = (int)(clipRect.Left / multiplier);
        if (minY * multiplier < clipRect.Top)
            minY = (int)(clipRect.Top / multiplier);

        dx = (int) fdx;
        dy = (int) fdy;

        return true;
    }

    private void OnPaintWithBackgroundImplEmpty(PaintEventArgs e) {
        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    private void OnPaintWithBackgroundImplDrawLoading(PaintEventArgs e) {
        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));
            using var brush = new SolidBrush(ForeColor);
            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            bufferedGraphics.Graphics.DrawString(LoadingText, Font, brush, ClientRectangle, stringFormat);
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    private void OnPaintWithBackgroundImplDrawExceptions(PaintEventArgs e, IEnumerable<Exception?> exceptions) {
        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));
            base.OnPaintBackground(e);

            using var brush = new SolidBrush(ForeColor);
            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            bufferedGraphics.Graphics.DrawString(
                $"Error displaying {File?.Name}." +
                string.Join(null, exceptions.Select(x => x is null ? "" : $"\n{x}")),
                Font,
                brush,
                ClientRectangle,
                stringFormat);
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
    }

    private void OnPaintWithBackground(PaintEventArgs e) {
        if (!TryGetRenderers(out var renderers, true)) {
            if (_renderers?.IsFaulted is true)
                OnPaintWithBackgroundImplDrawExceptions(
                    e,
                    _renderers.Exception?.InnerExceptions.AsEnumerable() ?? Array.Empty<Exception>());
            else
                OnPaintWithBackgroundImplDrawLoading(e);
            return;
        }

        if (MouseActivity.IsDragging)
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);

        foreach (var r in renderers) {
            switch (r.State) {
                case ITexRenderer.LoadState.Error:
                    continue;

                case ITexRenderer.LoadState.Empty:
                    if (FileResourceTyped is { } fr) {
                        MouseActivity.Enabled = false;
                        r.LoadTexFileAsync(fr, _currentMipmap)
                            .ContinueWith(res => {
                                MouseActivity.Enabled = true;
                                Viewport.Reset(r.ImageSize);

                                if (res.IsCompletedSuccessfully)
                                    ImageLoadedForDisplay?.Invoke(this, EventArgs.Empty);

                                ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
                                Invalidate();
                            }, TaskScheduler.FromCurrentSynchronizationContext());
                    }

                    if (!r.HasNondisposedBitmap)
                        break;

                    goto case ITexRenderer.LoadState.Loading;

                case ITexRenderer.LoadState.Loading:
                case ITexRenderer.LoadState.Loaded:
                    if (r.Draw(e))
                        return;
                    continue;
            }
        }
        
        if (FileResourceTyped is not null && renderers.All(x => x.State != ITexRenderer.LoadState.Loading))
            OnPaintWithBackgroundImplDrawExceptions(e, renderers.Select(x => x.LastException));
        else if (_loadingFileNameWhenEmpty is not null)
            OnPaintWithBackgroundImplDrawLoading(e);
        else
            OnPaintWithBackgroundImplEmpty(e);
    }

    protected sealed override void OnPaintBackground(PaintEventArgs e) { }

    protected sealed override void OnPaint(PaintEventArgs e) => OnPaintWithBackground(e);

    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (!_autoDescriptionBeingHovered) {
            if (AutoDescriptionRectangle.Contains(e.Location)) {
                _autoDescriptionBeingHovered = true;
                Invalidate(AutoDescriptionRectangle);
            }
        } else {
            _autoDescriptionBeingHovered = false;
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        }
    }

    protected override void OnMouseLeave(EventArgs e) {
        base.OnMouseLeave(e);
        if (_autoDescriptionBeingHovered) {
            _autoDescriptionBeingHovered = false;
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        }
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
        Size.Add(CreateGridLayout(_currentMipmap).GridSize, new(Margin.Horizontal, Margin.Vertical));

    public void ChangeDisplayedMipmap(int mipmap, bool force = false) {
        if (!force && _currentMipmap == mipmap)
            return;

        _currentMipmap = mipmap;

        ClearDisplayInformationCache();
        if (TryGetRenderers(out var renderers, true))
            foreach (var r in renderers)
                r.Reset(false);

        Invalidate();
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();
        ChangeDisplayedMipmap(0);
    }

    public override void ClearFile(bool keepContentsDisplayed = false) {
        ClearFileImpl();
        
        if (TryGetRenderers(out var renderers))
            foreach (var r in renderers)
                r.Reset(!keepContentsDisplayed);
        
        if (!keepContentsDisplayed)
            Viewport.Reset(Size.Empty);

        base.ClearFile(keepContentsDisplayed);
    }

    public void ExtendDescriptionMandatoryDisplay(TimeSpan duration) {
        var now = Environment.TickCount64;
        _autoDescriptionShowUntilTicks = Math.Max(
            _autoDescriptionShowUntilTicks,
            now + (long) duration.TotalMilliseconds);

        if (_autoDescriptionShowUntilTicks <= now) {
            _autoDescriptionFadeTimer.Enabled = false;
            return;
        }

        _autoDescriptionFadeTimer.Enabled = true;
        _autoDescriptionFadeTimer.Interval = 1;
        Invalidate(AutoDescriptionRectangle);
    }

    private void ClearDisplayInformationCache() {
        _autoDescriptionCached = null;
        _autoDescriptionSourceZoom = float.NaN;
        _autoDescriptionRectangle = null;
    }

    private void ClearFileImpl() {
        MouseActivity.Enabled = false;
        ClearDisplayInformationCache();
        _currentMipmap = -1;
    }

    private bool TryGetRenderers([MaybeNullWhen(false)] out ITexRenderer[] renderers, bool startLoading = false) {
        if (_renderers?.IsCompletedSuccessfully is true) {
            renderers = _renderers.Result;
            return true;
        }

        renderers = null;
        if (startLoading) {
            _renderers ??= RunOnUiThreadAfter(Task.Run(() => new ITexRenderer[] {
                new D2DTexRenderer(this),
                new GdipRenderer(this),
            }), r => {
                Invalidate();
                return r.Result;
            });
        }

        return false;
    }
}
