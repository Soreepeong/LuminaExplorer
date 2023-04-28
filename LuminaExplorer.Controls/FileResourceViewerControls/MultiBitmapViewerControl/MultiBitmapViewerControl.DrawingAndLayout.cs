using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private readonly BufferedGraphicsContext _bufferedGraphicsContext = new();

    private ResultDisposingTask<IBitmapSource>? _bitmapSourceTaskPrevious;
    private ResultDisposingTask<IBitmapSource>? _bitmapSourceTaskCurrent;
    private Task<ITexRenderer[]>? _renderers;

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
    private Tex2DShader.VisibleColorChannelTypes _visibleColorChannel;
    private bool _useAlphaChannel;
    private float _rotation;
    private IReadOnlyList<Tuple<Size, float>> _fontSizeStepLevel = new[] {
        Tuple.Create(new Size(480, 360), 9f),
        Tuple.Create(new Size(720, 540), 15f),
        Tuple.Create(new Size(1280, 720), 18f),
        Tuple.Create(new Size(1920, 1080), 30f),
        Tuple.Create(new Size(2560, 1440), 36f),
        Tuple.Create(new Size(3840, 2160), 60f),
    };
    
    public event EventHandler? RotationChanged;
    
    public event EventHandler? ViewportChanged;

    public event EventHandler? FontSizeStepLevelChanged;

    public event EventHandler? ForeColorWhenLoadedChanged;

    public event EventHandler? BackColorWhenLoadedChanged;

    public event EventHandler? BorderColorChanged;

    public event EventHandler? TransparencyCellColor1Changed;

    public event EventHandler? TransparencyCellColor2Changed;

    public event EventHandler? TransparencyCellSizeChanged;


    public event EventHandler? PixelGridLineColorChanged;

    // TODO: use this
    public bool UseAlphaChannel {
        get => _useAlphaChannel;
        set {
            if (_useAlphaChannel != value)
                return;
            _useAlphaChannel = value;
            Invalidate();
        }
    }

    // TODO: use this
    public Tex2DShader.VisibleColorChannelTypes VisibleColorChannel {
        get => _visibleColorChannel;
        set {
            if (_visibleColorChannel == value)
                return;
            _visibleColorChannel = value;
            Invalidate();
        }
    }

    // TODO: apply this to adjust viewport size too
    public float Rotation {
        get => _rotation;
        set {
            if (Equals(_rotation, value))
                return;
            _rotation = value;
            RotationChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public IReadOnlyList<Tuple<Size, float>> FontSizeStepLevel {
        get => _fontSizeStepLevel;
        set {
            if (Equals(_fontSizeStepLevel, value))
                return;
            _fontSizeStepLevel = value;
            FontSizeStepLevelChanged?.Invoke(this, EventArgs.Empty);
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
            TransparencyCellSizeChanged?.Invoke(this, EventArgs.Empty);
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

    public float EffectiveFontSizeScale =>
        _fontSizeStepLevel.LastOrDefault(
            x => x.Item1.Width <= ClientSize.Width && x.Item1.Height <= ClientSize.Height,
            _fontSizeStepLevel.FirstOrDefault(Tuple.Create(Size.Empty, Font.Size))).Item2 / 9 * DeviceDpi / 96;

    public float EffectiveFontSizeInPoints =>
        _fontSizeStepLevel.LastOrDefault(
            x => x.Item1.Width <= ClientSize.Width && x.Item1.Height <= ClientSize.Height,
            _fontSizeStepLevel.FirstOrDefault(Tuple.Create(Size.Empty, Font.Size))).Item2 / 9 * Font.SizeInPoints;

    public float AutoDescriptionOpacity {
        get {
            var d = _autoDescriptionShowUntilTicks - Environment.TickCount64;
            return _autoDescriptionBeingHovered ? 1f :
                d <= 0 ? 0f :
                d >= FadeOutDurationMs ? 1f : (float) d / FadeOutDurationMs;
        }
    }

    public Rectangle AutoDescriptionRectangle {
        get {
            if (TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    if (r.AutoDescriptionRectangle is { } rc)
                        return Rectangle.Truncate(rc);
            
            return Rectangle.Empty;
        }
    }
    
    private PanZoomTracker Viewport { get; }

    public PointF Pan {
        get => Viewport.Pan;
        set {
            if (Viewport.Pan != value)
                return;
            Viewport.Pan = value;
            Invalidate();
        }
    }

    public RectangleF EffectiveRect => Viewport.EffectiveRect;

    public SizeF EffectiveSize => Viewport.EffectiveSize;

    public float EffectiveZoom => Viewport.EffectiveZoom;

    private void OnViewportChanged() {
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        Invalidate();
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
                new D2DTexRenderer(this),
                new GdipTexRenderer(this),
            }), r => {
                Invalidate();
                foreach (var renderer in r.Result) {
                    renderer.UiThreadInitialize();
                    renderer.AnyBitmapSourceSliceLoadAttemptFinished +=
                        RendererOnAnyBitmapSourceSliceLoadAttemptFinished;
                    renderer.AllBitmapSourceSliceLoadAttemptFinished +=
                        RendererOnAllBitmapSourceSliceLoadAttemptFinished;
                }

                return r.Result;
            });
        }

        return false;
    }

    private void RendererOnAllBitmapSourceSliceLoadAttemptFinished(Task<IBitmapSource> obj) {
        if (_bitmapSourceTaskPrevious is not null) {
            if (TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    r.PreviousSourceTask = null;
            SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
        }
    }

    private void RendererOnAnyBitmapSourceSliceLoadAttemptFinished(Task<IBitmapSource> task) {
        if (_bitmapSourceTaskCurrent?.Task != task)
            return;
        BeginInvoke(() => {
            if (TryGetRenderers(out var renderers)) {
                MouseActivity.Enabled = true;
                foreach (var r in renderers)
                    if (r.LastException is null)
                        Viewport.Reset(task.Result.Layout.GridSize);
            }
        });
    }
}
