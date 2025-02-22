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

    private IReadOnlyList<Tuple<Size, float>> _fontSizeStepLevel = [
        Tuple.Create(new Size(480, 360), 9f),
        Tuple.Create(new Size(720, 540), 15f),
        Tuple.Create(new Size(1280, 720), 18f),
        Tuple.Create(new Size(1920, 1080), 30f),
        Tuple.Create(new Size(2560, 1440), 36f),
        Tuple.Create(new Size(3840, 2160), 60f),
    ];

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
        get => this._useAlphaChannel;
        set {
            if (this._useAlphaChannel == value)
                return;
            this._useAlphaChannel = value;
            this.UseAlphaChannelChanged?.Invoke(this, EventArgs.Empty);
            this.ClearDisplayInformationCache();
            this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
            this.Invalidate();
        }
    }

    public DirectXTexRendererShader.VisibleColorChannelTypes ChannelFilter {
        get => this._channelFilter;
        set {
            if (this._channelFilter == value)
                return;
            this._channelFilter = value;
            this.VisibleColorChannelChanged?.Invoke(this, EventArgs.Empty);
            this.ClearDisplayInformationCache();
            this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
            this.Invalidate();
        }
    }

    public float Rotation {
        get => this.Viewport.Rotation;
        set {
            if (Equals(this.Viewport.Rotation, value))
                return;

            this.Viewport.Rotation = value;
            this.RotationChanged?.Invoke(this, EventArgs.Empty);
            this.ClearDisplayInformationCache();
            this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
            this.Invalidate();
        }
    }

    public IReadOnlyList<Tuple<Size, float>> FontSizeStepLevel {
        get => this._fontSizeStepLevel;
        set {
            if (Equals(this._fontSizeStepLevel, value))
                return;
            this._fontSizeStepLevel = value;
            this.FontSizeStepLevelChanged?.Invoke(this, EventArgs.Empty);
            this.ClearDisplayInformationCache();
            this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
            this.Invalidate();
        }
    }

    public Color ForeColorWhenLoaded {
        get => this._foreColorWhenLoaded;
        set {
            if (this._foreColorWhenLoaded == value)
                return;
            this._foreColorWhenLoaded = value;
            this.ForeColorWhenLoadedChanged?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public Color BackColorWhenLoaded {
        get => this._backColorWhenLoaded;
        set {
            if (this._backColorWhenLoaded == value)
                return;
            this._backColorWhenLoaded = value;
            this.BackColorWhenLoadedChanged?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public Color TransparencyCellColor1 {
        get => this._transparencyCellColor1;
        set {
            if (this._transparencyCellColor1 == value)
                return;
            this._transparencyCellColor1 = value;
            this.TransparencyCellColor1Changed?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public Color TransparencyCellColor2 {
        get => this._transparencyCellColor2;
        set {
            if (this._transparencyCellColor2 == value)
                return;
            this._transparencyCellColor2 = value;
            this.TransparencyCellColor2Changed?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public int TransparencyCellSize {
        get => this._transparencyCellSize;
        set {
            if (this._transparencyCellSize == value)
                return;
            this._transparencyCellSize = value;
            this.TransparencyCellSizeChanged?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public Padding PanExtraRange {
        get => this.Viewport.PanExtraRange;
        set => this.Viewport.PanExtraRange = value;
    }

    public TimeSpan DelayShowingLoadingBoxFor { get; set; } = TimeSpan.FromMilliseconds(300);

    public bool IsLoadingBoxDelayed =>
        this._loadStartTicks == long.MaxValue || this._loadStartTicks + this.DelayShowingLoadingBoxFor.Milliseconds >
        Environment.TickCount64;

    public float OverlayBackgroundOpacity {
        get => this._overlayBackgroundOpacity;
        set {
            if (!Equals(this._overlayBackgroundOpacity, value))
                return;
            this._overlayBackgroundOpacity = value;
            this.Invalidate();
        }
    }

    public float NearestNeighborMinimumZoom {
        get => this._nearestNeighborMinimumZoom;
        set {
            if (Equals(this._nearestNeighborMinimumZoom, value))
                return;
            this._nearestNeighborMinimumZoom = value;
            this.Invalidate();
        }
    }

    public Color PixelGridLineColor {
        get => this._pixelGridLineColor;
        set {
            if (this._pixelGridLineColor == value)
                return;
            this._pixelGridLineColor = value;
            this.PixelGridLineColorChanged?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public float PixelGridMinimumZoom {
        get => this._pixelGridMinimumZoom;
        set {
            if (Equals(this._pixelGridMinimumZoom, value))
                return;
            this._pixelGridMinimumZoom = value;
            this.PixelGridMinimumZoomChanged?.Invoke(this, EventArgs.Empty);
            this.Invalidate();
        }
    }

    public Size SliceSpacing {
        get => this._sliceSpacing;
        set {
            if (this._sliceSpacing == value)
                return;

            this._sliceSpacing = value;

            this._bitmapSourceTaskPrevious?.Task.ContinueWith(
                r => {
                    r.Result.SliceSpacing = value;
                    this.Invalidate();
                },
                this.UiTaskScheduler);
            this._bitmapSourceTaskCurrent?.Task.ContinueWith(
                r => {
                    r.Result.SliceSpacing = value;
                    this.Invalidate();
                },
                this.UiTaskScheduler);
        }
    }

    public float EffectiveFontSizeScale =>
        this._fontSizeStepLevel.LastOrDefault(
            x => x.Item1.Width <= this.ClientSize.Width && x.Item1.Height <= this.ClientSize.Height,
            this._fontSizeStepLevel.FirstOrDefault(Tuple.Create(Size.Empty, this.Font.Size))).Item2 / 9 *
        this.DeviceDpi / 96;

    public float EffectiveFontSizeInPoints =>
        this._fontSizeStepLevel.LastOrDefault(
            x => x.Item1.Width <= this.ClientSize.Width && x.Item1.Height <= this.ClientSize.Height,
            this._fontSizeStepLevel.FirstOrDefault(Tuple.Create(Size.Empty, this.Font.Size))).Item2 / 9 *
        this.Font.SizeInPoints;

    public float AutoDescriptionOpacity {
        get {
            var d = this._autoDescriptionShowUntilTicks - Environment.TickCount64;
            return this._autoDescriptionBeingHovered ? 1f :
                d <= 0 ? 0f :
                d >= FadeOutDurationMs ? 1f : (float) d / FadeOutDurationMs;
        }
    }

    public Rectangle AutoDescriptionRectangle {
        get {
            if (this.TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    if (r.AutoDescriptionRectangle is { } rc)
                        return Rectangle.Truncate(rc);

            return Rectangle.Empty;
        }
    }

    private PanZoomTracker Viewport { get; }

    public PointF Pan {
        get => this.Viewport.Pan;
        set {
            if (this.Viewport.Pan != value)
                return;
            this.Viewport.Pan = value;
            this.Invalidate();
        }
    }

    public RectangleF EffectiveRect => this.Viewport.EffectiveRect;

    public SizeF EffectiveSize => this.Viewport.EffectiveSize;

    public SizeF EffectiveRotatedSize => this.Viewport.EffectiveRotatedSize;

    public float EffectiveZoom => this.Viewport.EffectiveZoom;

    private void OnViewportChanged()
    {
        this.ViewportChanged?.Invoke(this, EventArgs.Empty);
        this.ClearDisplayInformationCache();
        this.ExtendDescriptionMandatoryDisplay(this._fadeOutDelay);
        this.Invalidate();
    }

    private bool TryGetRenderers([MaybeNullWhen(false)] out ITexRenderer[] renderers, bool startLoading = false)
    {
        if (this._renderers?.IsCompletedSuccessfully is true) {
            renderers = this._renderers.Result;
            return true;
        }

        renderers = null;
        if (startLoading) {
            if (this._renderers?.IsFaulted is true) this._renderers = null;
            this._renderers ??= this.RunOnUiThreadAfter(
                Task.Run(
                    () => new ITexRenderer[] {
                        new DirectXTexRenderer(this),
                        // new GdipTexRenderer(this),
                    }),
                r => {
                    this.Invalidate();
                    foreach (var renderer in r.Result) {
                        renderer.UiThreadInitialize();
                        renderer.AnyBitmapSourceSliceLoadAttemptFinished +=
                            this.RendererOnAnyBitmapSourceSliceLoadAttemptFinished;
                    }

                    return r.Result;
                });
        }

        return false;
    }

    private void RendererOnAnyBitmapSourceSliceLoadAttemptFinished(Task<IBitmapSource> task)
    {
        if (this._bitmapSourceTaskCurrent?.Task != task)
            return;

        this.Invoke(
            () => {
                if (this._bitmapSourceTaskPrevious is not null) {
                    if (this.TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.PreviousSourceTask = null;
                    SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
                }

                this.MouseActivity.Enabled = true;
                this.Viewport.Reset(task.Result.Layout.GridSize, 0f);
                this.Invalidate();
            });
    }
}
