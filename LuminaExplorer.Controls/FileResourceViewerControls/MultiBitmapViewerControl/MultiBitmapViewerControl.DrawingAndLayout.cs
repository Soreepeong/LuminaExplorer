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
    private Color _transparencyCellColor1 = Color.White;
    private Color _transparencyCellColor2 = Color.LightGray;
    private int _transparencyCellSize = 8;
    private float _nearestNeighborMinimumZoom = 2f;
    private Color _pixelGridLineColor = Color.LightGray.MultiplyOpacity(0.5f);
    private float _pixelGridMinimumZoom = 5f;
    private float _overlayBackgroundOpacity = 0.7f;
    private Size _sliceSpacing = new(16, 16);
    private DirectXTexRendererShader.VisibleColorChannelTypes _channelFilter;
    private bool _useAlphaChannel = true;
    private IReadOnlyList<Tuple<Size, float>> _fontSizeStepLevel = new[] {
        Tuple.Create(new Size(480, 360), 9f),
        Tuple.Create(new Size(720, 540), 15f),
        Tuple.Create(new Size(1280, 720), 18f),
        Tuple.Create(new Size(1920, 1080), 30f),
        Tuple.Create(new Size(2560, 1440), 36f),
        Tuple.Create(new Size(3840, 2160), 60f),
    };

    public event EventHandler? UseAlphaChannelChanged;

    public event EventHandler? VisibleColorChannelChanged;
    
    public event EventHandler? RotationChanged;
    
    public event EventHandler? ViewportChanged;

    public event EventHandler? FontSizeStepLevelChanged;

    public event EventHandler? ForeColorWhenLoadedChanged;

    public event EventHandler? BackColorWhenLoadedChanged;

    public event EventHandler? TransparencyCellColor1Changed;

    public event EventHandler? TransparencyCellColor2Changed;

    public event EventHandler? TransparencyCellSizeChanged;

    public event EventHandler? PixelGridLineColorChanged;

    public event EventHandler? PixelGridMinimumZoomChanged;

    public bool UseAlphaChannel {
        get => _useAlphaChannel;
        set {
            if (_useAlphaChannel == value)
                return;
            _useAlphaChannel = value;
            UseAlphaChannelChanged?.Invoke(this, EventArgs.Empty);
            ClearDisplayInformationCache();
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
            Invalidate();
        }
    }

    public DirectXTexRendererShader.VisibleColorChannelTypes ChannelFilter {
        get => _channelFilter;
        set {
            if (_channelFilter == value)
                return;
            _channelFilter = value;
            VisibleColorChannelChanged?.Invoke(this, EventArgs.Empty);
            ClearDisplayInformationCache();
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
            Invalidate();
        }
    }

    public float Rotation {
        get => Viewport.Rotation;
        set {
            if (Equals(Viewport.Rotation, value))
                return;
            
            Viewport.Rotation = value;
            RotationChanged?.Invoke(this, EventArgs.Empty);
            ClearDisplayInformationCache();
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
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
            ClearDisplayInformationCache();
            ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
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
            PixelGridMinimumZoomChanged?.Invoke(this, EventArgs.Empty);
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

    public SizeF EffectiveRotatedSize => Viewport.EffectiveRotatedSize;

    public float EffectiveZoom => Viewport.EffectiveZoom;

    private void OnViewportChanged() {
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        ClearDisplayInformationCache();
        ExtendDescriptionMandatoryDisplay(_fadeOutDelay);
        Invalidate();
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
                new DirectXTexRenderer(this),
                // new GdipTexRenderer(this),
            }), r => {
                Invalidate();
                foreach (var renderer in r.Result) {
                    renderer.UiThreadInitialize();
                    renderer.AnyBitmapSourceSliceLoadAttemptFinished +=
                        RendererOnAnyBitmapSourceSliceLoadAttemptFinished;
                }

                return r.Result;
            });
        }

        return false;
    }

    private void RendererOnAnyBitmapSourceSliceLoadAttemptFinished(Task<IBitmapSource> task) {
        if (_bitmapSourceTaskCurrent?.Task != task)
            return;
        
        Invoke(() => {
            if (_bitmapSourceTaskPrevious is not null) {
                if (TryGetRenderers(out var renderers))
                    foreach (var r in renderers)
                        r.PreviousSourceTask = null;
                SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
            }
            
            MouseActivity.Enabled = true;
            Viewport.Reset(task.Result.Layout.GridSize, 0f);
            Invalidate();
        });
    }
}
