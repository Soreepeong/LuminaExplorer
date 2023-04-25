using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DirectN;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;

internal sealed class D2DTexRenderer : BaseD2DRenderer<MultiBitmapViewerControl>, ITexRenderer {
    private readonly TaskScheduler _taskScheduler;

    private IComObject<ID2D1Brush>? _foreColorWhenLoadedBrush;
    private IComObject<ID2D1Brush>? _backColorWhenLoadedBrush;
    private IComObject<ID2D1Brush>? _borderColorBrush;
    private IComObject<ID2D1Brush>? _transparencyCellColor1Brush;
    private IComObject<ID2D1Brush>? _transparencyCellColor2Brush;
    private IComObject<ID2D1Brush>? _pixelGridLineColorBrush;
    private IComObject<IDWriteTextFormat>? _scalingFontTextFormat;

    private RectangleF? _autoDescriptionRectangle;

    private readonly SourceSet?[] _sourceSets = new SourceSet?[2];

    public D2DTexRenderer(MultiBitmapViewerControl control, TaskScheduler scheduler) : base(control) {
        _taskScheduler = scheduler;
        Control.Resize += ControlOnResize;
        Control.FontSizeStepLevelChanged += ControlOnFontSizeStepLevelChanged;
        Control.ForeColorWhenLoadedChanged += ControlOnForeColorWhenLoadedChanged;
        Control.BackColorWhenLoadedChanged += ControlOnBackColorWhenLoadedChanged;
        Control.BorderColorChanged += ControlOnBorderColorChanged;
        Control.TransparencyCellColor1Changed += ControlOnTransparencyCellColor1Changed;
        Control.TransparencyCellColor2Changed += ControlOnTransparencyCellColor2Changed;
        Control.PixelGridLineColorChanged += ControlOnPixelGridLineColorChanged;
    }

    private IComObject<IDWriteTextFormat> ScalingFontTextFormat =>
        _scalingFontTextFormat ??= DWriteFactory.CreateTextFormat(
            familyName: Control.Font.FontFamily.Name,
            size: Control.EffectiveFontSizeInPoints * 4 / 3,
            weight: Control.Font.Bold
                ? DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD
                : DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
            style: Control.Font.Italic
                ? DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC
                : DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            localeName: "");

    public RectangleF? AutoDescriptionRectangle {
        get {
            if (_autoDescriptionRectangle is not null)
                return _autoDescriptionRectangle.Value;
            var padding = Control.Padding;
            var margin = Control.Margin;
            var rc = new RectangleF(
                padding.Left + margin.Left,
                padding.Top + margin.Top,
                Control.ClientSize.Width - padding.Horizontal - margin.Horizontal,
                Control.ClientSize.Height - padding.Vertical - margin.Vertical);

            using var textLayout = LayoutText(
                out var metrics,
                Control.AutoDescription,
                rc,
                DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
                DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
                DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
                ScalingFontTextFormat);
            rc.Width = metrics.width;
            rc.Height = metrics.height;
            return _autoDescriptionRectangle = rc;
        }
        set => _autoDescriptionRectangle = value;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Control.Resize -= ControlOnResize;
            Control.FontSizeStepLevelChanged -= ControlOnFontSizeStepLevelChanged;
            Control.ForeColorWhenLoadedChanged -= ControlOnForeColorWhenLoadedChanged;
            Control.BackColorWhenLoadedChanged -= ControlOnBackColorWhenLoadedChanged;
            Control.BorderColorChanged -= ControlOnBorderColorChanged;
            Control.TransparencyCellColor1Changed -= ControlOnTransparencyCellColor1Changed;
            Control.TransparencyCellColor2Changed -= ControlOnTransparencyCellColor2Changed;
            Control.PixelGridLineColorChanged -= ControlOnPixelGridLineColorChanged;
        }

        UpdateBitmapSource(null, null);
        SafeDispose.One(ref _foreColorWhenLoadedBrush);
        SafeDispose.One(ref _backColorWhenLoadedBrush);
        SafeDispose.One(ref _borderColorBrush);
        SafeDispose.One(ref _transparencyCellColor1Brush);
        SafeDispose.One(ref _transparencyCellColor2Brush);
        SafeDispose.One(ref _pixelGridLineColorBrush);
        SafeDispose.One(ref _scalingFontTextFormat);

        base.Dispose(disposing);
    }

    private SourceSet? SourcePrevious {
        get => _sourceSets[0];
        set => _sourceSets[0] = value;
    }

    private SourceSet? SourceCurrent {
        get => _sourceSets[1];
        set => _sourceSets[1] = value;
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;

    private IComObject<ID2D1Brush> BackColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _backColorWhenLoadedBrush, Control.BackColorWhenLoaded);

    private IComObject<ID2D1Brush> ForeColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _foreColorWhenLoadedBrush, Control.ForeColorWhenLoaded);

    private IComObject<ID2D1Brush> ContentBorderColorBrush =>
        GetOrCreateSolidColorBrush(ref _borderColorBrush, Control.ContentBorderColor);

    private IComObject<ID2D1Brush> TransparencyCellColor1Brush =>
        GetOrCreateSolidColorBrush(ref _transparencyCellColor1Brush, Control.TransparencyCellColor1);

    private IComObject<ID2D1Brush> TransparencyCellColor2Brush =>
        GetOrCreateSolidColorBrush(ref _transparencyCellColor2Brush, Control.TransparencyCellColor2);

    private IComObject<ID2D1Brush> PixelGridLineColorBrush =>
        GetOrCreateSolidColorBrush(ref _pixelGridLineColorBrush, Control.PixelGridLineColor);

    public bool UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current) {
        LastException = null;

        if (previous == current)
            previous = null;

        var changed = false;
        if (SourcePrevious?.SourceTask == current) {
            if (SourceCurrent?.SourceTask == previous) {
                // swap
                (SourceCurrent, SourcePrevious) = (SourcePrevious, SourceCurrent);
                return true;
            }

            // move from prev to current
            SourceCurrent?.Dispose();
            (SourceCurrent, SourcePrevious) = (SourcePrevious, null);
            changed = true;
        } else if (SourceCurrent?.SourceTask == previous) {
            // move from curr to prev
            SourcePrevious?.Dispose();
            (SourcePrevious, SourceCurrent) = (SourceCurrent, null);
            changed = true;
        }

        if (previous != SourcePrevious?.SourceTask) {
            SourcePrevious?.Dispose();
            SourcePrevious = previous is null ? null : new(this, previous);
            changed = true;
        }

        if (current != SourceCurrent?.SourceTask) {
            SourceCurrent?.Dispose();
            SourceCurrent = current is null ? null : new(this, current);
            changed = true;
        }

        return changed;
    }

    public bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask) =>
        bitmapSourceTask is not null && (
            (bitmapSourceTask == SourceCurrent?.SourceTask && SourceCurrent.IsAnyVisibleSliceReadyForDrawing()) ||
            (bitmapSourceTask == SourcePrevious?.SourceTask && SourcePrevious.IsAnyVisibleSliceReadyForDrawing()));

    public bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask) =>
        bitmapSourceTask is not null && (
            (bitmapSourceTask == SourceCurrent?.SourceTask && SourceCurrent.IsEveryVisibleSliceReadyForDrawing()) ||
            (bitmapSourceTask == SourcePrevious?.SourceTask && SourcePrevious.IsEveryVisibleSliceReadyForDrawing()));

    protected override void DrawInternal() {
        var renderTarget = RenderTarget;

        var imageRect = Rectangle.Truncate(Control.ImageRect);
        var clientSize = Control.ClientSize;
        var overlayRect = new Rectangle(
            Control.Padding.Left + Control.Margin.Left,
            Control.Padding.Top + Control.Margin.Top,
            clientSize.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
            clientSize.Height - Control.Padding.Vertical - Control.Margin.Vertical);
        var box = Control.ClientRectangle.ToSilkFloat();

        if (!Control.TryGetEffectiveOverlayInformation(
                out var overlayString,
                out var overlayForeOpacity,
                out var overlayBackOpacity,
                out var hideIfNotLoading))
            overlayString = null;

        if (SourceCurrent?.IsAnyVisibleSliceReadyForDrawing() is true)
            renderTarget.Object.Clear(Control.BackColorWhenLoaded.ToD3Dcolorvalue());
        else if (SourceCurrent?.SourceTask.IsFaulted is true)
            renderTarget.Object.Clear(Control.BackColor.ToD3Dcolorvalue());
        else if (SourcePrevious?.IsAnyVisibleSliceReadyForDrawing() is true)
            renderTarget.Object.Clear(Control.BackColorWhenLoaded.ToD3Dcolorvalue());
        else
            renderTarget.Object.Clear(Control.BackColor.ToD3Dcolorvalue());

        var currentSourceFullyAvailable = SourceCurrent?.IsEveryVisibleSliceReadyForDrawing() is true;
        var isLoading = false;
        foreach (var sourceSet in _sourceSets) {
            if (sourceSet is null)
                continue;
            if (currentSourceFullyAvailable && SourcePrevious == sourceSet) {
                Debug.Assert(SourcePrevious != SourceCurrent);
                continue;
            }

            if (sourceSet.SourceTask.IsCompletedSuccessfully) {
                var source = sourceSet.SourceTask.Result;

                var bitmapLoaded = ArrayPool<LoadState>.Shared.Rent(source.Layout.Count);
                try {
                    foreach (var sliceCell in source.Layout)
                        sourceSet.TryGetBitmapAt(sliceCell, out bitmapLoaded[sliceCell.CellIndex], out _, out _);

                    // 1. Draw transparency grids
                    foreach (var sliceCell in source.Layout) {
                        if (bitmapLoaded[sliceCell.CellIndex] != LoadState.Loaded)
                            continue;

                        var cellRect = source.Layout.RectOf(sliceCell, imageRect);
                        if (Control.ShouldDrawTransparencyGrid(
                                cellRect,
                                Control.ClientRectangle,
                                out var multiplier,
                                out var minX,
                                out var minY,
                                out var dx,
                                out var dy)) {
                            var yLim = Math.Min(cellRect.Bottom, clientSize.Height);
                            var xLim = Math.Min(cellRect.Right, clientSize.Width);
                            TransparencyCellColor1Brush.Object.SetOpacity(1f);
                            TransparencyCellColor2Brush.Object.SetOpacity(1f);
                            for (var y = minY;; y++) {
                                box.top = y * multiplier + dy;
                                box.bottom = box.top + Math.Min(multiplier, yLim - box.top);
                                if (box.top >= box.bottom)
                                    break;

                                for (var x = minX;; x++) {
                                    box.left = x * multiplier + dx;
                                    box.right = box.left + Math.Min(multiplier, xLim - box.left);
                                    if (box.left >= box.right)
                                        break;

                                    renderTarget.Object.FillRectangle(
                                        ref box,
                                        (x + y) % 2 == 0
                                            ? TransparencyCellColor1Brush.Object
                                            : TransparencyCellColor2Brush.Object);
                                }
                            }
                        }
                    }

                    // 2. Draw cell borders
                    if (Control.ContentBorderWidth > 0) {
                        ContentBorderColorBrush.Object.SetOpacity(1f);
                        foreach (var sliceCell in source.Layout) {
                            if (bitmapLoaded[sliceCell.CellIndex] != LoadState.Loaded)
                                continue;

                            box = RectangleF.Inflate(
                                source.Layout.RectOf(sliceCell, imageRect),
                                Control.ContentBorderWidth / 2f,
                                Control.ContentBorderWidth / 2f).ToSilkFloat();
                            renderTarget.Object.DrawRectangle(
                                ref box,
                                ContentBorderColorBrush.Object,
                                Control.ContentBorderWidth,
                                null);
                        }
                    }

                    // 3. Draw bitmaps
                    foreach (var sliceCell in source.Layout) {
                        box = source.Layout.RectOf(sliceCell, imageRect).ToSilkFloat();

                        if (bitmapLoaded[sliceCell.CellIndex] == LoadState.Loaded) {
                            if (sourceSet.TryGetBitmapAt(
                                sliceCell,
                                out bitmapLoaded[sliceCell.CellIndex],
                                out var bitmap,
                                out _)) {
                                renderTarget.Object.DrawBitmap(
                                    bitmap.Object,
                                    opacity: 1f,
                                    interpolationMode: Control.NearestNeighborMinimumZoom <= Control.EffectiveZoom
                                        ? D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR
                                        : D2D1_BITMAP_INTERPOLATION_MODE.D2D1_BITMAP_INTERPOLATION_MODE_LINEAR,
                                    destinationRectangle: box);
                            }
                        }

                        switch (bitmapLoaded[sliceCell.CellIndex]) {
                            case LoadState.Loading:
                            case LoadState.Error:
                            case LoadState.Empty:
                            default: {
                                // TODO: show loading/error
                                break;
                            }
                        }
                    }

                    // 4. Draw pixel grids
                    if (Control.PixelGridMinimumZoom <= Control.EffectiveZoom) {
                        var p1 = new D2D_POINT_2F();
                        var p2 = new D2D_POINT_2F();

                        foreach (var sliceCell in source.Layout) {
                            if (bitmapLoaded[sliceCell.CellIndex] != LoadState.Loaded)
                                continue;

                            var cellRectUnscaled = source.Layout.RectOf(sliceCell);
                            var cellRect = source.Layout.RectOf(sliceCell, imageRect);

                            // 0 <= cellTop + j * cellHeight / sliceHeight < clientHeight
                            // -cellTop <= j * cellHeight / sliceHeight < clientHeight - cellTop
                            // -cellTop * sliceHeight / cellHeight <= j < (clientHeight - cellTop) * sliceHeight / cellHeight
                            // 0 <= j < cellRectUnscaled + 1

                            p1.x = cellRect.Left + 0.5f;
                            p2.x = cellRect.Right - 0.5f;
                            var rangeMin = Math.Max(0, (int) Math.Floor(1f *
                                -cellRect.Top * cellRectUnscaled.Height / cellRect.Height));
                            var rangeMax = Math.Min(cellRectUnscaled.Height + 1, (int) Math.Ceiling(1f *
                                (clientSize.Height - cellRect.Top) * cellRectUnscaled.Height / cellRect.Height));
                            for (var j = rangeMin; j < rangeMax; j++) {
                                var y = cellRect.Top + j * cellRect.Height / cellRectUnscaled.Height;
                                p1.y = p2.y = y + 0.5f;
                                renderTarget.Object.DrawLine(p1, p2, PixelGridLineColorBrush.Object, 1f, null);
                            }

                            p1.y = cellRect.Top + 0.5f;
                            p2.y = cellRect.Bottom - 0.5f;
                            rangeMin = Math.Max(0, (int) Math.Floor(1f *
                                -cellRect.Left * cellRectUnscaled.Width / cellRect.Width));
                            rangeMax = Math.Min(cellRectUnscaled.Width + 1, (int) Math.Ceiling(1f *
                                (clientSize.Width - cellRect.Left) * cellRectUnscaled.Width / cellRect.Width));
                            for (var j = rangeMin; j < rangeMax; j++) {
                                var x = cellRect.Left + j * cellRect.Width / cellRectUnscaled.Width;
                                p1.x = p2.x = x + 0.5f;
                                renderTarget.Object.DrawLine(p1, p2, PixelGridLineColorBrush.Object, 1f, null);
                            }
                        }
                    }
                } finally {
                    ArrayPool<LoadState>.Shared.Return(bitmapLoaded);
                }

                DrawText(
                    Control.AutoDescription,
                    overlayRect,
                    wordWrapping: DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
                    textAlignment: DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
                    paragraphAlignment: DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
                    textFormat: ScalingFontTextFormat,
                    textBrush: ForeColorWhenLoadedBrush,
                    shadowBrush: BackColorWhenLoadedBrush,
                    opacity: Control.AutoDescriptionOpacity,
                    borderWidth: 2);
            } else if (sourceSet.SourceTask.IsFaulted)
                overlayString = $"Error occurred loading the file.\n{sourceSet.SourceTask.Exception}";
            else
                isLoading = true;
        }

        if (hideIfNotLoading && !isLoading)
            overlayString = null;

        if (overlayString is null)
            return;

        var textLayout = LayoutText(
            out var metrics,
            overlayString,
            Control.ClientRectangle,
            wordWrapping: DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
            DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
            ScalingFontTextFormat);
        var fontSizeScale = Control.EffectiveFontSizeScale;
        box = new(
            metrics.left - 32 * fontSizeScale,
            metrics.top - 32 * fontSizeScale,
            metrics.left + metrics.width + 32 * fontSizeScale,
            metrics.top + metrics.height + 32 * fontSizeScale);

        try {
            BackColorBrush.Object.SetOpacity(overlayBackOpacity);
            ForeColorBrush.Object.SetOpacity(overlayForeOpacity);

            renderTarget.Object.FillRectangle(ref box, BackColorBrush.Object);

            for (var i = -2; i <= 2; i++) {
                for (var j = -2; j <= 2; j++) {
                    if (i == 0 && j == 0)
                        continue;

                    renderTarget.Object.DrawTextLayout(
                        new(i, j),
                        textLayout.Object,
                        BackColorBrush.Object,
                        D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
                }
            }

            renderTarget.Object.DrawTextLayout(
                new(),
                textLayout.Object,
                ForeColorBrush.Object,
                D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        } finally {
            SafeDispose.One(ref textLayout);
        }
    }

    private void ControlOnFontSizeStepLevelChanged(object? sender, EventArgs e) {
        SafeDispose.One(ref _scalingFontTextFormat);
    }

    private void ControlOnResize(object? sender, EventArgs e) {
        SafeDispose.One(ref _scalingFontTextFormat);
    }

    private void ControlOnForeColorWhenLoadedChanged(object? sender, EventArgs e) =>
        SafeDispose.One(ref _foreColorWhenLoadedBrush);

    private void ControlOnBackColorWhenLoadedChanged(object? sender, EventArgs e)
        => SafeDispose.One(ref _backColorWhenLoadedBrush);

    private void ControlOnBorderColorChanged(object? sender, EventArgs e) => SafeDispose.One(ref _borderColorBrush);

    private void ControlOnTransparencyCellColor1Changed(object? sender, EventArgs e) =>
        SafeDispose.One(ref _transparencyCellColor1Brush);

    private void ControlOnTransparencyCellColor2Changed(object? sender, EventArgs e) =>
        SafeDispose.One(ref _transparencyCellColor2Brush);

    private void ControlOnPixelGridLineColorChanged(object? sender, EventArgs e) =>
        SafeDispose.One(ref _pixelGridLineColorBrush);

    private sealed class SourceSet : IDisposable, IAsyncDisposable {
        private readonly D2DTexRenderer _renderer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        public readonly Task<IBitmapSource> SourceTask;

        private ResultDisposingTask<IComObject<ID2D1Bitmap>>?[ /* Image */][ /* Mip */][ /* Slice */]? _bitmaps;

        public SourceSet(D2DTexRenderer renderer, Task<IBitmapSource> sourceTask) {
            _renderer = renderer;
            SourceTask = sourceTask;
            SourceTask.ContinueWith(r => {
                if (!r.IsCompletedSuccessfully)
                    return;

                var source = r.Result;
                _ = SafeDispose.EnumerableAsync(ref _bitmaps);

                _bitmaps = new ResultDisposingTask<IComObject<ID2D1Bitmap>>[source.ImageCount][][];
                for (var i = 0; i < _bitmaps.Length; i++) {
                    var a1 = _bitmaps[i] = new ResultDisposingTask<IComObject<ID2D1Bitmap>>[source.NumberOfMipmaps(i)][];
                    for (var j = 0; j < a1.Length; j++)
                        a1[j] = new ResultDisposingTask<IComObject<ID2D1Bitmap>>[source.DepthOfMipmap(i, j)];
                }

                r.Result.LayoutChanged += SourceTaskOnLayoutChanged;
                SourceTaskOnLayoutChanged();
            });
        }

        public void Dispose() {
            _cancellationTokenSource.Cancel();
            _ = SafeDispose.EnumerableAsync(ref _bitmaps);
            SourceTask.ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.LayoutChanged -= SourceTaskOnLayoutChanged;
            });
        }

        public ValueTask DisposeAsync() {
            _cancellationTokenSource.Cancel();
            return new(Task.Run(Dispose));
        }

        public bool IsAnyVisibleSliceReadyForDrawing() =>
            SourceTask.IsCompletedSuccessfully &&
            SourceTask.Result.Layout.Any(
                x => _bitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        public bool IsEveryVisibleSliceReadyForDrawing() =>
            SourceTask.IsCompletedSuccessfully &&
            SourceTask.Result.Layout.All(
                x => _bitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        private LoadState TryGetBitmapWrapperTaskAt(
            GridLayoutCell cell,
            out ResultDisposingTask<IComObject<ID2D1Bitmap>>? wrapperTask,
            out Exception? exception) {
            wrapperTask = null;
            exception = null;

            if (SourceTask.IsCompletedSuccessfully is not true) {
                if (SourceTask.IsFaulted)
                    exception = SourceTask.Exception;
                return exception is null ? LoadState.Loading : LoadState.Error;
            }

            var source = SourceTask.Result;

            var task = source.GetWicBitmapSourceAsync(cell);
            if (task.IsFaulted) {
                exception = task.Exception;
                return LoadState.Error;
            }

            if (task.IsCompleted && _bitmaps is { } pBitmaps) {
                wrapperTask = pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice];
                if (wrapperTask is null) {
                    wrapperTask = pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice] = new(Task.Run(
                        () => _renderer.CreateFromWicBitmap(task.Result),
                        _cancellationTokenSource.Token));
                    wrapperTask.Task.ContinueWith(_ => _renderer.Control.Invalidate());
                }

                if (wrapperTask.IsCompletedSuccessfully)
                    return LoadState.Loaded;

                if (wrapperTask.IsFaulted) {
                    exception = wrapperTask.Task.Exception;
                    return LoadState.Error;
                }

                return LoadState.Loading;
            }

            return exception is null ? LoadState.Loading : LoadState.Error;
        }

        public bool TryGetBitmapAt(
            GridLayoutCell cell,
            out LoadState state,
            [MaybeNullWhen(false)] out IComObject<ID2D1Bitmap> pBitmap,
            out Exception? exception) {
            state = TryGetBitmapWrapperTaskAt(cell, out var task, out exception);
            pBitmap = state == LoadState.Loaded ? task!.Result : null;
            return pBitmap is not null;
        }

        private void SourceTaskOnLayoutChanged() {
            var layout = SourceTask.Result.Layout;
            var cell = layout[0];
            Task.Factory.StartNew(
                () => SourceTask.Result
                    .GetWicBitmapSourceAsync(cell)
                    .ContinueWith(result => {
                        if (this != _renderer.SourceCurrent || layout != SourceTask.Result.Layout)
                            return;

                        if (!result.IsCompletedSuccessfully || layout.Count == 0 || layout[0] != cell) {
                            _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                            return;
                        }

                        TryGetBitmapWrapperTaskAt(cell, out var wrapperTask, out _);
                        if (wrapperTask is null) {
                            _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                            return;
                        }

                        wrapperTask.Task.ContinueWith(_ => {
                            if (this != _renderer.SourceCurrent)
                                return;

                            _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                        });
                    }),
                _cancellationTokenSource.Token,
                TaskCreationOptions.None,
                _renderer._taskScheduler);
        }
    }
}
