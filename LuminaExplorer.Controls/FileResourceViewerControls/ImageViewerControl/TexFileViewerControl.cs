using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;
using Timer = System.Windows.Forms.Timer;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl : AbstractFileResourceViewerControl<TexFile> {
    private const int FadeOutDurationMs = 200;
    private readonly TimeSpan _fadeOutDelay = TimeSpan.FromSeconds(1);
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();

    public readonly PanZoomTracker Viewport;

    private ResultDisposingTask<IBitmapSource>? _bitmapSourceTaskPrevious;
    private ResultDisposingTask<IBitmapSource>? _bitmapSourceTaskCurrent;
    private Task<ITexRenderer[]>? _renderers;

    private int _currentImageIndex;
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
    private Color _pixelGridLineColor = Color.LightGray.MultiplyOpacity(0.5f);
    private float _pixelGridMinimumZoom = 5f;
    private float _overlayBackgroundOpacity = 0.7f;
    private Size _sliceSpacing = new(16, 16);

    private readonly Timer _fadeTimer;
    private long _autoDescriptionShowUntilTicks;
    private bool _autoDescriptionBeingHovered;
    private string? _autoDescriptionCached;
    private float _autoDescriptionSourceZoom = float.NaN;
    private Rectangle? _autoDescriptionRectangle;

    private string? _overlayCustomString;
    private long _overlayShowUntilTicks;

    private long _loadStartTicks = long.MaxValue;

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

        _fadeTimer = new();
        _fadeTimer.Enabled = false;
        _fadeTimer.Interval = 1;
        _fadeTimer.Tick += (_, _) => {
            var animating = false;
            var now = Environment.TickCount64;

            var autoDescriptionRemaining = _autoDescriptionShowUntilTicks - now;
            switch (autoDescriptionRemaining) {
                case < 0:
                    Invalidate(AutoDescriptionRectangle);
                    break;
                case < FadeOutDurationMs:
                    animating = true;
                    Invalidate(AutoDescriptionRectangle);
                    break;
            }

            var overlayRemaining = _overlayShowUntilTicks - now;
            switch (overlayRemaining) {
                case < 0:
                    Invalidate();
                    break;
                case < FadeOutDurationMs:
                    animating = true;
                    Invalidate();
                    break;
            }

            var loadingBoxRemainingUntilShow = _loadStartTicks == long.MaxValue
                ? int.MaxValue
                : (int) (_loadStartTicks + DelayShowingLoadingBoxFor.TotalMilliseconds - now);

            if (animating) {
                _fadeTimer.Interval = 1;
                return;
            }

            var next = int.MaxValue;
            if (autoDescriptionRemaining > 0)
                next = Math.Min(next, (int) (autoDescriptionRemaining - FadeOutDurationMs));
            if (overlayRemaining > 0)
                next = Math.Min(next, (int) (overlayRemaining - FadeOutDurationMs));
            if (loadingBoxRemainingUntilShow > 0)
                next = Math.Min(next, loadingBoxRemainingUntilShow);

            if (next == int.MaxValue)
                _fadeTimer.Enabled = false;
            else
                _fadeTimer.Interval = next;
        };

        TryGetRenderers(out _, true);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _bufferedGraphicsContext.Dispose();
            if (TryGetRenderers(out var renderers))
                _ = SafeDispose.EnumerableAsync(ref renderers);
            Viewport.Dispose();
            _fadeTimer.Dispose();
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            _ = SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
        }

        base.Dispose(disposing);
    }

    public event EventHandler? ForeColorWhenLoadedChanged;

    public event EventHandler? BackColorWhenLoadedChanged;

    public event EventHandler? BorderColorChanged;

    public event EventHandler? TransparencyCellColor1Changed;

    public event EventHandler? TransparencyCellColor2Changed;

    public event EventHandler? PixelGridLineColorChanged;

    public string? FileName { get; private set; }

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

    public TimeSpan DelayShowingLoadingBoxFor { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsLoadingBoxDelayed =>
        _loadStartTicks == long.MaxValue ||
        _loadStartTicks + DelayShowingLoadingBoxFor.Milliseconds > Environment.TickCount64;

    public float OverlayBackgroundOpacity {
        get => _overlayBackgroundOpacity;
        set {
            if (!Equals(_overlayBackgroundOpacity, value))
                return;
            _overlayBackgroundOpacity = value;
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

    public Color PixelGridLineColor {
        get => _pixelGridLineColor;
        set {
            if (_pixelGridLineColor == value)
                return;
            _pixelGridLineColor = value;
            PixelGridLineColorChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

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

            _bitmapSourceTaskPrevious?.Task.ContinueWith(r => {
                r.Result.SliceSpacing = value;
                Invalidate();
            }, UiTaskScheduler);
            _bitmapSourceTaskCurrent?.Task.ContinueWith(r => {
                r.Result.SliceSpacing = value;
                Invalidate();
            }, UiTaskScheduler);
        }
    }

    public string AutoDescription {
        get {
            var effectiveZoom = Viewport.EffectiveZoom;
            if (_autoDescriptionCached is not null && Equals(effectiveZoom, _autoDescriptionSourceZoom))
                return _autoDescriptionCached;

            var sb = new StringBuilder();
            _autoDescriptionSourceZoom = effectiveZoom;
            sb.AppendLine(FileName);

            if (PhysicalFile is { } physicalFile) {
                // TODO
            } else if (FileResourceTyped is { } texFile) {
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
            }

            return _autoDescriptionCached = sb.ToString();
        }
    }

    public float AutoDescriptionOpacity {
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

    public FileInfo? PhysicalFile { get; private set; }

    internal bool TryGetEffectiveOverlayInformation(
        out string s,
        out float foreOpacity,
        out float backOpacity,
        out bool hideIfNotLoading) {
        hideIfNotLoading = false;
        var now = Environment.TickCount64;
        var customOverlayVisible =
            !string.IsNullOrWhiteSpace(_overlayCustomString) &&
            _overlayShowUntilTicks > Environment.TickCount64;
        var hasLoadingText =
            FileName is not null || _loadingFileNameWhenEmpty is not null;

        if (customOverlayVisible) {
            var remaining = _overlayShowUntilTicks - now;
            if (remaining >= FadeOutDurationMs / 2 || !hasLoadingText) {
                s = _overlayCustomString!;
                foreOpacity = remaining >= FadeOutDurationMs ? 1f : 1f * remaining / FadeOutDurationMs;
                backOpacity = _overlayBackgroundOpacity * foreOpacity;
                return true;
            }
        }

        if (hasLoadingText) {
            s = string.IsNullOrWhiteSpace(FileName ?? _loadingFileNameWhenEmpty)
                ? "Loading..."
                : $"Loading {FileName ?? _loadingFileNameWhenEmpty}...";
            foreOpacity = 1f;
            backOpacity = _overlayBackgroundOpacity * foreOpacity;
            hideIfNotLoading = true;
            return true;
        }

        s = "";
        foreOpacity = backOpacity = 0f;
        return false;
    }

    internal bool ShouldDrawTransparencyGrid(
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
            ? (int) -MiscUtils.DivRem(cellRect.Left, -multiplier, out var fdx)
            : (int) MiscUtils.DivRem(cellRect.Left, multiplier, out fdx);
        minY = cellRect.Top < 0
            ? (int) -MiscUtils.DivRem(cellRect.Top, -multiplier, out var fdy)
            : (int) MiscUtils.DivRem(cellRect.Top, multiplier, out fdy);

        if (minX * multiplier < clipRect.Left)
            minX = (int) (clipRect.Left / multiplier);
        if (minY * multiplier < clipRect.Top)
            minY = (int) (clipRect.Top / multiplier);

        dx = (int) fdx;
        dy = (int) fdy;

        return true;
    }

    private void OnPaintWithBackground(PaintEventArgs e) {
        var exceptions = Array.Empty<Exception>();
        if (!TryGetRenderers(out var renderers, true)) {
            if (_renderers?.IsFaulted is true)
                exceptions = _renderers.Exception?.InnerExceptions.ToArray() ??
                             new Exception[] {new("Failed to load any renderer for unknown reasons.")};
        } else {
            if (MouseActivity.IsDragging)
                ExtendDescriptionMandatoryDisplay(_fadeOutDelay);

            var hasException = false;
            foreach (var r in renderers) {
                if (r.LastException is not null) {
                    hasException = true;
                    continue;
                }

                if (r.Draw(e))
                    return;
            }

            if (hasException)
                exceptions = renderers.Where(x => x.LastException is not null).Select(x => x.LastException!).ToArray();
        }

        BufferedGraphics? bufferedGraphics = null;
        try {
            bufferedGraphics = _bufferedGraphicsContext.Allocate(e.Graphics, e.ClipRectangle);
            base.OnPaintBackground(new(bufferedGraphics.Graphics, e.ClipRectangle));

            using var brush = new SolidBrush(ForeColor);
            using var stringFormat = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            if (exceptions.Any()) {
                bufferedGraphics.Graphics.DrawString(
                    $"Error displaying {FileName}.\n\n" +
                    string.Join('\n', exceptions.Select(x => x.ToString())),
                    Font,
                    brush,
                    ClientRectangle,
                    stringFormat);
            } else if (TryGetEffectiveOverlayInformation(out var overlayText, out _, out _, out _)) {
                bufferedGraphics.Graphics.DrawString(overlayText, Font, brush, ClientRectangle, stringFormat);
            }
        } finally {
            bufferedGraphics?.Render();
            bufferedGraphics?.Dispose();
        }
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
        Size.Add(
            _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                ? _bitmapSourceTaskCurrent.Result.Layout.GridSize
                : base.GetPreferredSize(proposedSize),
            new(Margin.Horizontal, Margin.Vertical));

    public override async Task<Size> GetPreferredSizeAsync(Size proposedSize) {
        Size? size = null;
        if (_bitmapSourceTaskCurrent is not null) {
            try {
                size = (await _bitmapSourceTaskCurrent.ConfigureAwait(false)).Layout.GridSize;
            } catch (Exception) {
                // pass
            }
        }

        size ??= await base.GetPreferredSizeAsync(proposedSize);

        return Size.Add(size.Value, new(Margin.Horizontal, Margin.Vertical));
    }

    public void ChangeDisplayedMipmap(int imageIndex, int mipmap, bool force = false) {
        if (_bitmapSourceTaskCurrent is not { } bitmapSource)
            return;

        if (!force && _currentMipmap == mipmap && _currentImageIndex == imageIndex)
            return;

        _currentMipmap = mipmap;
        _currentImageIndex = imageIndex;
        _loadStartTicks = Environment.TickCount64;
        _fadeTimer.Enabled = true;
        _fadeTimer.Interval = 1;
        MouseActivity.Enabled = false;

        ClearDisplayInformationCache();
        bitmapSource.Task.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully ||
                bitmapSource != _bitmapSourceTaskCurrent ||
                _currentMipmap != mipmap ||
                _currentImageIndex != imageIndex)
                return;

            r.Result.UpdateSelection(imageIndex, mipmap);
        }, UiTaskScheduler);

        Invalidate();
    }

    public void SetFile(FileInfo fileInfo) {
        ClearFileImpl();
        PhysicalFile = fileInfo;

        throw new NotImplementedException();
        SetFileImpl(fileInfo.Name, Task.FromResult((IBitmapSource) new TexBitmapSource(FileResourceTyped!)));
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();

        SetFileImpl(file.Name, Task.FromResult((IBitmapSource) new TexBitmapSource(FileResourceTyped!)));
    }

    private void SetFileImpl(string fileName, Task<IBitmapSource> sourceTask) {
        FileName = fileName;

        if (_bitmapSourceTaskCurrent is not null) {
            if (IsCurrentBitmapSourceReadyOnRenderer()) {
                if (TryGetRenderers(out var renderers))
                    foreach (var renderer in renderers)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                _bitmapSourceTaskCurrent = null;
            } else {
                if (TryGetRenderers(out var renderers))
                    foreach (var renderer in renderers)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            }
        }

        var sourceTaskCurrent = _bitmapSourceTaskCurrent = new(sourceTask);

        {
            if (TryGetRenderers(out var renderers, true)) {
                foreach (var renderer in renderers)
                    renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
            } else {
                _renderers!.ContinueWith(result => {
                    if (sourceTaskCurrent != _bitmapSourceTaskCurrent || !result.IsCompletedSuccessfully)
                        return;

                    foreach (var renderer in result.Result)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
                }, UiTaskScheduler);
            }
        }

        ChangeDisplayedMipmap(0, 0);
    }

    public override void ClearFile(bool keepContentsDisplayed = false) {
        ClearFileImpl();

        if (keepContentsDisplayed) {
            if (_bitmapSourceTaskCurrent is not null) {
                if (IsCurrentBitmapSourceReadyOnRenderer()) {
                    if (TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                    SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                    _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                    _bitmapSourceTaskCurrent = null;
                } else {
                    if (TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                    SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
                }
            }
        } else {
            if (TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    r.UpdateBitmapSource(null, null);

            SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
            SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            Viewport.Reset(Size.Empty);
        }

        base.ClearFile(keepContentsDisplayed);
    }

    private bool IsCurrentBitmapSourceReadyOnRenderer() =>
        _bitmapSourceTaskCurrent is {IsCompletedSuccessfully: true} sourceTask &&
        TryGetRenderers(out var renderers) &&
        renderers.Any(r => r.LastException is null && r.HasBitmapSourceReadyForDrawing(sourceTask.Task));

    public void ExtendDescriptionMandatoryDisplay(TimeSpan duration) {
        var now = Environment.TickCount64;
        _autoDescriptionShowUntilTicks = Math.Max(
            _autoDescriptionShowUntilTicks,
            now + (long) duration.TotalMilliseconds);

        if (_autoDescriptionShowUntilTicks <= now)
            return;

        _fadeTimer.Enabled = true;
        _fadeTimer.Interval = 1;
        Invalidate(AutoDescriptionRectangle);
    }

    public void ShowOverlayString(string? overlayString, TimeSpan overlayTextMessageDuration) {
        var now = Environment.TickCount64;
        _overlayCustomString = overlayString;
        _overlayShowUntilTicks = now + (int) overlayTextMessageDuration.TotalMilliseconds;

        if (_overlayShowUntilTicks <= now)
            return;

        _fadeTimer.Enabled = true;
        _fadeTimer.Interval = 1;
        Invalidate();
    }

    private void ClearDisplayInformationCache() {
        _autoDescriptionCached = null;
        _autoDescriptionSourceZoom = float.NaN;
        _autoDescriptionRectangle = null;
    }

    private void ClearFileImpl() {
        MouseActivity.Enabled = false;
        _loadStartTicks = long.MaxValue;
        FileName = null;
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
            if (_renderers?.IsFaulted is true)
                _renderers = null;
            _renderers ??= RunOnUiThreadAfter(Task.Run(() => new ITexRenderer[] {
                new D2DTexRenderer(this, UiTaskScheduler),
                // new GdipTexRenderer(this),
            }), r => {
                Invalidate();
                foreach (var renderer in r.Result) {
                    renderer.UiThreadInitialize();
                    renderer.AnyBitmapSourceSliceAvailableForDrawing +=
                        RendererOnAnyBitmapSourceSliceAvailableForDrawing;
                }

                return r.Result;
            });
        }

        return false;
    }

    private void RendererOnAnyBitmapSourceSliceAvailableForDrawing(Task<IBitmapSource> task) {
        if (_bitmapSourceTaskCurrent?.Task != task)
            return;
        BeginInvoke(() => {
            if (TryGetRenderers(out var renderers)) {
                MouseActivity.Enabled = true;
                foreach (var r in renderers)
                    if (r.LastException is null)
                        Viewport.Reset(new(task.Result.Width, task.Result.Height));
            }
        });
    }
}
