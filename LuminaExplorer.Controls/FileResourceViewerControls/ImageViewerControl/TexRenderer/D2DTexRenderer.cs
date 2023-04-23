using System;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DirectWrite;
using Silk.NET.Maths;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;

internal sealed unsafe class D2DTexRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
    private readonly TaskScheduler _taskScheduler;

    private ID2D1Brush* _pForeColorWhenLoadedBrush;
    private ID2D1Brush* _pBackColorWhenLoadedBrush;
    private ID2D1Brush* _pBorderColorBrush;
    private ID2D1Brush* _pTransparencyCellColor1Brush;
    private ID2D1Brush* _pTransparencyCellColor2Brush;
    private ID2D1Brush* _pPixelGridLineColorBrush;

    private SourceSet? _sourcePrevious;
    private SourceSet? _sourceCurrent;

    public D2DTexRenderer(TexFileViewerControl control, TaskScheduler scheduler) : base(control) {
        _taskScheduler = scheduler;
        Control.ForeColorWhenLoadedChanged += ControlOnForeColorWhenLoadedChanged;
        Control.BackColorWhenLoadedChanged += ControlOnBackColorWhenLoadedChanged;
        Control.BorderColorChanged += ControlOnBorderColorChanged;
        Control.TransparencyCellColor1Changed += ControlOnTransparencyCellColor1Changed;
        Control.TransparencyCellColor2Changed += ControlOnTransparencyCellColor2Changed;
        Control.PixelGridLineColorChanged += ControlOnPixelGridLineColorChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Control.ForeColorWhenLoadedChanged -= ControlOnForeColorWhenLoadedChanged;
            Control.BackColorWhenLoadedChanged -= ControlOnBackColorWhenLoadedChanged;
            Control.BorderColorChanged -= ControlOnBorderColorChanged;
            Control.TransparencyCellColor1Changed -= ControlOnTransparencyCellColor1Changed;
            Control.TransparencyCellColor2Changed -= ControlOnTransparencyCellColor2Changed;
            Control.PixelGridLineColorChanged -= ControlOnPixelGridLineColorChanged;
        }

        UpdateBitmapSource(null, null);
        SafeRelease(ref _pForeColorWhenLoadedBrush);
        SafeRelease(ref _pBackColorWhenLoadedBrush);
        SafeRelease(ref _pBorderColorBrush);
        SafeRelease(ref _pTransparencyCellColor1Brush);
        SafeRelease(ref _pTransparencyCellColor2Brush);
        SafeRelease(ref _pPixelGridLineColorBrush);

        base.Dispose(disposing);
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;

    private ID2D1Brush* BackColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _pBackColorWhenLoadedBrush, Control.BackColorWhenLoaded);

    private ID2D1Brush* ForeColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _pForeColorWhenLoadedBrush, Control.ForeColorWhenLoaded);

    private ID2D1Brush* ContentBorderColorBrush =>
        GetOrCreateSolidColorBrush(ref _pBorderColorBrush, Control.ContentBorderColor);

    private ID2D1Brush* TransparencyCellColor1Brush =>
        GetOrCreateSolidColorBrush(ref _pTransparencyCellColor1Brush, Control.TransparencyCellColor1);

    private ID2D1Brush* TransparencyCellColor2Brush =>
        GetOrCreateSolidColorBrush(ref _pTransparencyCellColor2Brush, Control.TransparencyCellColor2);

    private ID2D1Brush* PixelGridLineColorBrush =>
        GetOrCreateSolidColorBrush(ref _pPixelGridLineColorBrush, Control.PixelGridLineColor);

    private LoadState TryGetActiveSourceSet(
        out SourceSet sourceSet,
        out IBitmapSource source,
        out TaskWrappingIUnknown<ID2D1Bitmap>?[] slices,
        out Exception? exception) {
        sourceSet = null!;
        source = null!;
        slices = null!;
        exception = null;

        Exception? currentException = null;
        Exception? previousException = null;

        var stateCurrent = _sourceCurrent?.TryGetSlices(out slices, out currentException) ?? LoadState.Empty;
        if (stateCurrent is LoadState.Loaded) {
            sourceSet = _sourceCurrent!;
            source = _sourceCurrent!.SourceTask.Result;
            return stateCurrent;
        }

        var statePrevious = _sourcePrevious?.TryGetSlices(out slices, out previousException) ?? LoadState.Empty;
        if (statePrevious is LoadState.Loaded) {
            sourceSet = _sourcePrevious!;
            source = _sourcePrevious!.SourceTask.Result;
            return statePrevious;
        }

        exception = stateCurrent switch {
            LoadState.Error => currentException,
            LoadState.Empty when statePrevious == LoadState.Error => previousException,
            _ => null,
        };
        return stateCurrent;
    }

    public void UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current) {
        LastException = null;

        if (previous == current)
            previous = null;

        if (_sourcePrevious?.SourceTask == current) {
            if (_sourceCurrent?.SourceTask == previous) {
                // swap
                (_sourceCurrent, _sourcePrevious) = (_sourcePrevious, _sourceCurrent);
                return;
            }

            // move from prev to current
            SafeDispose.OneAsync(ref _sourceCurrent);
            (_sourceCurrent, _sourcePrevious) = (_sourcePrevious, null);
        } else if (_sourceCurrent?.SourceTask == previous) {
            // move from curr to prev
            SafeDispose.OneAsync(ref _sourcePrevious);
            (_sourcePrevious, _sourceCurrent) = (_sourceCurrent, null);
        }

        if (previous != _sourcePrevious?.SourceTask) {
            SafeDispose.OneAsync(ref _sourcePrevious);
            _sourcePrevious = previous is null ? null : new(this, previous);
        }

        if (current != _sourceCurrent?.SourceTask) {
            SafeDispose.OneAsync(ref _sourceCurrent);
            _sourceCurrent = current is null ? null : new(this, current);
        }
    }

    public bool HasBitmapSourceReadyForDrawing(Task<IBitmapSource> bitmapSourceTask) =>
        (bitmapSourceTask == _sourceCurrent?.SourceTask && _sourceCurrent.IsEverySliceReadyForDrawing()) ||
        (bitmapSourceTask == _sourcePrevious?.SourceTask && _sourcePrevious.IsEverySliceReadyForDrawing());

    protected override void DrawInternal() {
        var pRenderTarget = RenderTarget;

        var imageRect = Rectangle.Truncate(Control.Viewport.EffectiveRect);
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

        var loadState = TryGetActiveSourceSet(
            out var sourceSet,
            out var source,
            out var slices,
            out var exception);
        switch (loadState) {
            case LoadState.Loaded: {
                BackColorWhenLoadedBrush->SetOpacity(1f);
                pRenderTarget->FillRectangle(&box, BackColorWhenLoadedBrush);

                if (Control.ContentBorderWidth > 0)
                    ContentBorderColorBrush->SetOpacity(1f);

                // 1. Draw transparency grids
                for (var i = 0; i < slices.Length; i++) {
                    var cellRect = source.Layout.RectOf(i, imageRect);
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
                        TransparencyCellColor1Brush->SetOpacity(1f);
                        TransparencyCellColor2Brush->SetOpacity(1f);
                        for (var y = minY;; y++) {
                            box.Min.Y = y * multiplier + dy;
                            box.Max.Y = box.Min.Y + Math.Min(multiplier, yLim - box.Min.Y);
                            if (box.Min.Y >= box.Max.Y)
                                break;

                            for (var x = minX;; x++) {
                                box.Min.X = x * multiplier + dx;
                                box.Max.X = box.Min.X + Math.Min(multiplier, xLim - box.Min.X);
                                if (box.Min.X >= box.Max.X)
                                    break;

                                pRenderTarget->FillRectangle(
                                    &box,
                                    (x + y) % 2 == 0
                                        ? TransparencyCellColor1Brush
                                        : TransparencyCellColor2Brush);
                            }
                        }
                    }
                }

                // 2. Draw cell borders
                if (Control.ContentBorderWidth > 0) {
                    for (var i = 0; i < slices.Length; i++) {
                        box = RectangleF.Inflate(
                            source.Layout.RectOf(i, imageRect),
                            Control.ContentBorderWidth / 2f,
                            Control.ContentBorderWidth / 2f).ToSilkFloat();
                        pRenderTarget->DrawRectangle(&box, ContentBorderColorBrush, Control.ContentBorderWidth, null);
                    }
                }

                // 3. Draw bitmaps
                for (var i = 0; i < slices.Length; i++) {
                    box = source.Layout.RectOf(i, imageRect).ToSilkFloat();
                    var loadState2 = sourceSet.TryGetBitmapAt(
                        i,
                        out var pBitmap,
                        out exception);

                    switch (loadState2) {
                        case LoadState.Loaded: {
                            pRenderTarget->DrawBitmap(
                                pBitmap,
                                &box,
                                1f, // opacity
                                Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                                    ? BitmapInterpolationMode.NearestNeighbor
                                    : BitmapInterpolationMode.Linear,
                                null);
                            break;
                        }
                        case LoadState.Loading:
                            if (sourceSet == _sourceCurrent && _sourcePrevious is not null) {
                                if (_sourcePrevious.TryGetBitmapAt(i, out pBitmap, out _) == LoadState.Loaded) {
                                    pRenderTarget->DrawBitmap(
                                        pBitmap,
                                        &box,
                                        1f, // opacity
                                        Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                                            ? BitmapInterpolationMode.NearestNeighbor
                                            : BitmapInterpolationMode.Linear,
                                        null);
                                }
                            }

                            goto case LoadState.Error;
                        case LoadState.Error:
                        case LoadState.Empty:
                        default: {
                            // TODO: show loading/error
                            break;
                        }
                    }
                }

                // 4. Draw pixel grids
                if (Control.PixelGridMinimumZoom <= Control.Viewport.EffectiveZoom) {
                    var p1 = new Vector2D<float>();
                    var p2 = new Vector2D<float>();

                    for (var i = 0; i < slices.Length; i++) {
                        var cellRectUnscaled = source.Layout.RectOf(i);
                        var cellRect = source.Layout.RectOf(i, imageRect);

                        p1.X = cellRect.Left + 0.5f;
                        p2.X = cellRect.Right - 0.5f;
                        for (var j = cellRectUnscaled.Height - 1; j >= 0; j--) {
                            var y = cellRect.Top + j * cellRect.Height / cellRectUnscaled.Height;
                            p1.Y = p2.Y = y + 0.5f;
                            pRenderTarget->DrawLine(p1, p2, PixelGridLineColorBrush, 1f, null);
                        }

                        p1.Y = cellRect.Top + 0.5f;
                        p2.Y = cellRect.Bottom - 0.5f;
                        for (var j = cellRectUnscaled.Width - 1; j >= 0; j--) {
                            var x = cellRect.Left + j * cellRect.Width / cellRectUnscaled.Width;
                            p1.X = p2.X = x + 0.5f;
                            pRenderTarget->DrawLine(p1, p2, PixelGridLineColorBrush, 1f, null);
                        }
                    }
                }

                DrawText(
                    Control.AutoDescription,
                    overlayRect,
                    wordWrapping: WordWrapping.EmergencyBreak,
                    textAlignment: TextAlignment.Leading,
                    paragraphAlignment: ParagraphAlignment.Near,
                    textBrush: ForeColorWhenLoadedBrush,
                    shadowBrush: BackColorWhenLoadedBrush,
                    opacity: Control.AutoDescriptionOpacity,
                    borderWidth: 2);
                break;
            }
            case LoadState.Loading:
            case LoadState.Error:
            case LoadState.Empty:
            default: {
                BackColorBrush->SetOpacity(1f);
                pRenderTarget->FillRectangle(&box, BackColorBrush);

                if (overlayString is null && exception is not null)
                    overlayString = $"Error occurred loading the file.\n{exception}";
                break;
            }
        }

        if (hideIfNotLoading && loadState != LoadState.Loading)
            overlayString = null;

        if (overlayString is null)
            return;

        box = Control.ClientRectangle.ToSilkFloat();
        var textLayout = LayoutText(
            out var metrics,
            overlayString,
            Control.ClientRectangle,
            WordWrapping.EmergencyBreak,
            TextAlignment.Center,
            ParagraphAlignment.Center,
            FontTextFormat);
        box = new(
            metrics.Left - 32,
            metrics.Top - 32,
            metrics.Left + metrics.Width + 32,
            metrics.Top + metrics.Height + 32);

        try {
            BackColorBrush->SetOpacity(overlayBackOpacity);
            ForeColorBrush->SetOpacity(overlayForeOpacity);

            pRenderTarget->FillRectangle(&box, BackColorBrush);

            for (var i = -2; i <= 2; i++) {
                for (var j = -2; j <= 2; j++) {
                    if (i == 0 && j == 0)
                        continue;

                    pRenderTarget->DrawTextLayout(
                        new(i, j),
                        (Silk.NET.Direct2D.IDWriteTextLayout*) textLayout,
                        BackColorBrush,
                        DrawTextOptions.None);
                }
            }

            pRenderTarget->DrawTextLayout(
                new(),
                (Silk.NET.Direct2D.IDWriteTextLayout*) textLayout,
                ForeColorBrush,
                DrawTextOptions.None);
        } finally {
            SafeRelease(ref textLayout);
        }
    }

    private void ControlOnForeColorWhenLoadedChanged(object? sender, EventArgs e) =>
        SafeRelease(ref _pForeColorWhenLoadedBrush);

    private void ControlOnBackColorWhenLoadedChanged(object? sender, EventArgs e)
        => SafeRelease(ref _pBackColorWhenLoadedBrush);

    private void ControlOnBorderColorChanged(object? sender, EventArgs e) => SafeRelease(ref _pBorderColorBrush);

    private void ControlOnTransparencyCellColor1Changed(object? sender, EventArgs e) =>
        SafeRelease(ref _pTransparencyCellColor1Brush);

    private void ControlOnTransparencyCellColor2Changed(object? sender, EventArgs e) =>
        SafeRelease(ref _pTransparencyCellColor2Brush);

    private void ControlOnPixelGridLineColorChanged(object? sender, EventArgs e) =>
        SafeRelease(ref _pPixelGridLineColorBrush);

    private sealed class SourceSet : IDisposable, IAsyncDisposable {
        private readonly D2DTexRenderer _renderer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        public readonly Task<IBitmapSource> SourceTask;

        private TaskWrappingIUnknown<ID2D1Bitmap>[ /* Image */]?[ /* Mip */]?[ /* Slice */]? _pBitmaps;

        public SourceSet(D2DTexRenderer renderer, Task<IBitmapSource> sourceTask) {
            _renderer = renderer;
            SourceTask = sourceTask;
            SourceTask.ContinueWith(r => {
                if (r.IsCompletedSuccessfully) {
                    r.Result.ImageOrMipmapChanged += SourceTaskOnImageOrMipmapChanged;
                    SourceTaskOnImageOrMipmapChanged();
                }
            });
        }

        public void Dispose() {
            _cancellationTokenSource.Cancel();
            _ = SafeDispose.EnumerableAsync(ref _pBitmaps);
            SourceTask.ContinueWith(r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.ImageOrMipmapChanged -= SourceTaskOnImageOrMipmapChanged;
            });
        }

        public ValueTask DisposeAsync() {
            _cancellationTokenSource.Cancel();
            return new(Task.Run(Dispose));
        }

        public bool IsEverySliceReadyForDrawing() =>
            SourceTask.IsCompletedSuccessfully &&
            Enumerable.Range(0, SourceTask.Result.Depth).All(SourceTask.Result.HasWicBitmapSource);

        public LoadState TryGetSlices(out TaskWrappingIUnknown<ID2D1Bitmap>?[] slices, out Exception? exception) {
            slices = null!;
            exception = null;

            if (SourceTask.IsCompletedSuccessfully is not true) {
                if (SourceTask.IsFaulted)
                    exception = SourceTask.Exception;
                return exception is null ? LoadState.Loading : LoadState.Error;
            }

            var source = SourceTask.Result;
            if (_pBitmaps?.Length != source.ImageCount) {
                _ = SafeDispose.EnumerableAsync(ref _pBitmaps);
                _pBitmaps = new TaskWrappingIUnknown<ID2D1Bitmap>[source.ImageCount][][];
            }

            var mipmaps = _pBitmaps[source.ImageIndex];
            if (mipmaps?.Length != source.NumMipmaps) {
                _ = SafeDispose.EnumerableAsync(ref _pBitmaps[source.ImageIndex]);
                _pBitmaps[source.ImageIndex] = mipmaps = new TaskWrappingIUnknown<ID2D1Bitmap>[source.NumMipmaps][];
            }

            var nullableSlices = mipmaps[source.Mipmap];
            if (nullableSlices?.Length != source.Depth) {
                _ = SafeDispose.EnumerableAsync(ref mipmaps[source.Mipmap]);
                nullableSlices = mipmaps[source.Mipmap] = new TaskWrappingIUnknown<ID2D1Bitmap>[source.Depth];
            }

            slices = nullableSlices;
            return LoadState.Loaded;
        }

        public LoadState TryGetBitmapWrapperTaskAt(
            int slice,
            out TaskWrappingIUnknown<ID2D1Bitmap>? wrapperTask,
            out Exception? exception) {
            wrapperTask = null;
            exception = null;

            var state = TryGetSlices(out var slices, out exception);
            if (state is not LoadState.Loaded)
                return state;

            var source = SourceTask.Result;

            // This case happens when previous image does not have enough depth when current image is being loaded.
            if (slice >= source.Depth)
                return LoadState.Loading;

            var task = source.GetWicBitmapSourceAsync(slice);
            if (task.IsFaulted) {
                exception = task.Exception;
                return LoadState.Error;
            }

            if (task.IsCompleted) {
                wrapperTask = slices[slice] ??= new(Task.Run(() => {
                    var ptr = new ComPtr<ID2D1Bitmap>();
                    // do NOT merge into constructor, or it will do AddRef, which we do not want.
                    _renderer.GetOrCreateFromWicBitmap(ref ptr.Handle, task.Result);
                    return ptr;
                }, _cancellationTokenSource.Token));

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

        public LoadState TryGetBitmapAt(
            int slice,
            out ID2D1Bitmap* pBitmap,
            out Exception? exception) {
            var state = TryGetBitmapWrapperTaskAt(slice, out var task, out exception);
            pBitmap = state == LoadState.Loaded ? task!.Result : null;
            return state;
        }

        private void SourceTaskOnImageOrMipmapChanged() => Task.Factory.StartNew(
            () => SourceTask.Result
                .GetWicBitmapSourceAsync(0)
                .ContinueWith(result => {
                    if (this != _renderer._sourceCurrent)
                        return;

                    if (!result.IsCompletedSuccessfully) {
                        _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                        return;
                    }

                    TryGetBitmapWrapperTaskAt(0, out var wrapperTask, out _);
                    if (wrapperTask is null) {
                        _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                        return;
                    }

                    wrapperTask.Task.ContinueWith(_ => {
                        if (this != _renderer._sourceCurrent)
                            return;
                        
                        _renderer.AnyBitmapSourceSliceAvailableForDrawing?.Invoke(SourceTask);
                    });
                }),
            _cancellationTokenSource.Token,
            TaskCreationOptions.None,
            _renderer._taskScheduler);
    }

    private sealed class TaskWrappingIUnknown<T> : IDisposable, IAsyncDisposable
        where T : unmanaged, IComVtbl<T> {
        public readonly Task<ComPtr<T>> Task;

        public TaskWrappingIUnknown(Task<ComPtr<T>> task) {
            Task = task;
        }

        public bool IsCompletedSuccessfully => Task.IsCompletedSuccessfully;
        public bool IsCompleted => Task.IsCompleted;
        public bool IsCanceled => Task.IsCanceled;
        public bool IsFaulted => Task.IsFaulted;
        public TaskStatus Status => Task.Status;
        public T* Result => Task.Result.Handle;

        public ConfiguredTaskAwaitable<ComPtr<T>> ConfigureAwait(bool continueOnCapturedContext) =>
            Task.ConfigureAwait(continueOnCapturedContext);

        public void Dispose() {
            Task.ContinueWith(result => {
                result.Result.Release();
                ;
            });
        }

        public ValueTask DisposeAsync() => new(Task.ContinueWith(result => {
            result.Result.Release();
            return ValueTask.CompletedTask;
        }));
    }
}
