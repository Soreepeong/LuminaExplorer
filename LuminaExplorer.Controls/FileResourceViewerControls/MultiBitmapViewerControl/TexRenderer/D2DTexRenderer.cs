using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.Maths;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;

internal sealed unsafe class D2DTexRenderer : BaseD2DRenderer<MultiBitmapViewerControl>, ITexRenderer {
    private ID2D1Brush* _pForeColorWhenLoadedBrush;
    private ID2D1Brush* _pBackColorWhenLoadedBrush;
    private ID2D1Brush* _pBorderColorBrush;
    private ID2D1Brush* _pTransparencyCellColor1Brush;
    private ID2D1Brush* _pTransparencyCellColor2Brush;
    private ID2D1Brush* _pPixelGridLineColorBrush;
    private IDWriteTextFormat* _pScalingFontTextFormat;

    private RectangleF? _autoDescriptionRectangle;

    private readonly SourceSet?[] _sourceSets = new SourceSet?[2];

    public D2DTexRenderer(MultiBitmapViewerControl control) : base(control) {
        Control.Resize += ControlOnResize;
        Control.FontSizeStepLevelChanged += ControlOnFontSizeStepLevelChanged;
        Control.ForeColorWhenLoadedChanged += ControlOnForeColorWhenLoadedChanged;
        Control.BackColorWhenLoadedChanged += ControlOnBackColorWhenLoadedChanged;
        Control.BorderColorChanged += ControlOnBorderColorChanged;
        Control.TransparencyCellColor1Changed += ControlOnTransparencyCellColor1Changed;
        Control.TransparencyCellColor2Changed += ControlOnTransparencyCellColor2Changed;
        Control.PixelGridLineColorChanged += ControlOnPixelGridLineColorChanged;
    }

    private void ControlOnFontSizeStepLevelChanged(object? sender, EventArgs e) {
        SafeRelease(ref _pScalingFontTextFormat);
    }

    private void ControlOnResize(object? sender, EventArgs e) {
        SafeRelease(ref _pScalingFontTextFormat);
    }

    private IDWriteTextFormat* ScalingFontTextFormat {
        get {
            if (_pScalingFontTextFormat is null)
                fixed (char* pName = Control.Font.Name.AsSpan())
                fixed (char* pEmpty = "\0".AsSpan())
                fixed (IDWriteTextFormat** ppFontTextFormat = &_pScalingFontTextFormat)
                    ThrowH(DWriteFactory->CreateTextFormat(
                        pName,
                        null,
                        Control.Font.Bold ? FontWeight.Bold : FontWeight.Normal,
                        Control.Font.Italic ? FontStyle.Italic : FontStyle.Normal,
                        FontStretch.Normal,
                        Control.EffectiveFontSizeInPoints * 4 / 3,
                        pEmpty,
                        ppFontTextFormat));
            return _pScalingFontTextFormat;
        }
    }

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

            var textLayout = LayoutText(
                out var metrics,
                Control.AutoDescription,
                rc,
                WordWrapping.EmergencyBreak,
                TextAlignment.Leading,
                ParagraphAlignment.Near,
                ScalingFontTextFormat);
            try {
                rc.Width = metrics.Width;
                rc.Height = metrics.Height;
                return _autoDescriptionRectangle = rc;
            } finally {
                textLayout->Release();
            }
        }
        set => _autoDescriptionRectangle = value;
    }

    public Task<IBitmapSource>? PreviousSourceTask {
        get => SourcePrevious?.SourceTask;
        set => UpdateBitmapSource(value, CurrentSourceTask);
    }

    public Task<IBitmapSource>? CurrentSourceTask {
        get => SourceCurrent?.SourceTask;
        set => UpdateBitmapSource(PreviousSourceTask, value);
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
        SafeRelease(ref _pForeColorWhenLoadedBrush);
        SafeRelease(ref _pBackColorWhenLoadedBrush);
        SafeRelease(ref _pBorderColorBrush);
        SafeRelease(ref _pTransparencyCellColor1Brush);
        SafeRelease(ref _pTransparencyCellColor2Brush);
        SafeRelease(ref _pPixelGridLineColorBrush);
        SafeRelease(ref _pScalingFontTextFormat);

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

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceLoadAttemptFinished;

    public event Action<Task<IBitmapSource>>? AllBitmapSourceSliceLoadAttemptFinished;

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

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        var context = SharedD3D11Context;
        var colors = ArrayPool<float>.Shared.Rent(4);
        try {
            Color color;
            if (SourceCurrent?.IsAnyVisibleSliceReadyForDrawing() is true)
                color = Control.BackColorWhenLoaded;
            else if (SourceCurrent?.SourceTask.IsFaulted is true)
                color = Control.BackColor;
            else if (SourcePrevious?.IsAnyVisibleSliceReadyForDrawing() is true)
                color = Control.BackColorWhenLoaded;
            else
                color = Control.BackColor;

            colors[0] = 1f * color.R / 255;
            colors[1] = 1f * color.G / 255;
            colors[2] = 1f * color.B / 255;
            colors[3] = 1f * color.A / 255;

            context->ClearRenderTargetView(pRenderTarget, ref colors[0]);
        } finally {
            ArrayPool<float>.Shared.Return(colors);
        }
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget) {
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
                var bitmapExceptions = ArrayPool<Exception?>.Shared.Rent(source.Layout.Count);
                try {
                    foreach (var sliceCell in source.Layout)
                        bitmapLoaded[sliceCell.CellIndex] = sourceSet.TryGetBitmapAt(
                            sliceCell,
                            out _,
                            out bitmapExceptions[sliceCell.CellIndex]);

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
                        ContentBorderColorBrush->SetOpacity(1f);
                        foreach (var sliceCell in source.Layout) {
                            if (bitmapLoaded[sliceCell.CellIndex] != LoadState.Loaded)
                                continue;

                            box = RectangleF.Inflate(
                                source.Layout.RectOf(sliceCell, imageRect),
                                Control.ContentBorderWidth / 2f,
                                Control.ContentBorderWidth / 2f).ToSilkFloat();
                            pRenderTarget->DrawRectangle(&box, ContentBorderColorBrush, Control.ContentBorderWidth,
                                null);
                        }
                    }

                    // 3. Draw bitmaps
                    foreach (var sliceCell in source.Layout) {
                        var rc = source.Layout.RectOf(sliceCell, imageRect);
                        box = rc.ToSilkFloat();

                        if (bitmapLoaded[sliceCell.CellIndex] == LoadState.Loaded) {
                            bitmapLoaded[sliceCell.CellIndex] =
                                sourceSet.TryGetBitmapAt(
                                    sliceCell,
                                    out var pBitmap,
                                    out bitmapExceptions[sliceCell.CellIndex]);
                            if (bitmapLoaded[sliceCell.CellIndex] == LoadState.Loaded) {
                                pRenderTarget->DrawBitmap(
                                    pBitmap,
                                    &box,
                                    1f, // opacity
                                    Control.NearestNeighborMinimumZoom <= Control.EffectiveZoom
                                        ? BitmapInterpolationMode.NearestNeighbor
                                        : BitmapInterpolationMode.Linear,
                                    null);
                                continue;
                            }
                        }

                        if (bitmapLoaded[sliceCell.CellIndex] is not LoadState.Error and LoadState.Loading)
                            continue;
                        
                        var isError = bitmapLoaded[sliceCell.CellIndex] == LoadState.Error;
                        string msg;
                        if (isError) {
                            BackColorBrush->SetOpacity(Control.OverlayBackgroundOpacity);
                            pRenderTarget->FillRectangle(&box, BackColorBrush);
                            msg = $"Error\n({sliceCell.ImageIndex}, {sliceCell.Mipmap}, {sliceCell.Slice})\n";
                        } else if (Control.IsLoadingBoxDelayed)
                            continue;
                        else
                            msg = "Loading...";

                        DrawText(
                            msg,
                            rc,
                            wordWrapping: WordWrapping.EmergencyBreak,
                            textAlignment: TextAlignment.Center,
                            paragraphAlignment: ParagraphAlignment.Center,
                            textFormat: ScalingFontTextFormat,
                            textBrush: ForeColorBrush,
                            shadowBrush: BackColorBrush,
                            opacity: 1f,
                            borderWidth: 2);
                    }

                    // 4. Draw pixel grids
                    if (Control.PixelGridMinimumZoom <= Control.EffectiveZoom) {
                        var p1 = new Vector2D<float>();
                        var p2 = new Vector2D<float>();

                        foreach (var sliceCell in source.Layout) {
                            if (bitmapLoaded[sliceCell.CellIndex] != LoadState.Loaded)
                                continue;

                            var cellRectUnscaled = source.Layout.RectOf(sliceCell);
                            var cellRect = source.Layout.RectOf(sliceCell, imageRect);

                            // 0 <= cellTop + j * cellHeight / sliceHeight < clientHeight
                            // -cellTop <= j * cellHeight / sliceHeight < clientHeight - cellTop
                            // -cellTop * sliceHeight / cellHeight <= j < (clientHeight - cellTop) * sliceHeight / cellHeight
                            // 0 <= j < cellRectUnscaled + 1

                            p1.X = cellRect.Left + 0.5f;
                            p2.X = cellRect.Right - 0.5f;
                            var rangeMin = Math.Max(0, (int) Math.Floor(1f *
                                -cellRect.Top * cellRectUnscaled.Height / cellRect.Height));
                            var rangeMax = Math.Min(cellRectUnscaled.Height + 1, (int) Math.Ceiling(1f *
                                (clientSize.Height - cellRect.Top) * cellRectUnscaled.Height / cellRect.Height));
                            for (var j = rangeMin; j < rangeMax; j++) {
                                var y = cellRect.Top + j * cellRect.Height / cellRectUnscaled.Height;
                                p1.Y = p2.Y = y + 0.5f;
                                pRenderTarget->DrawLine(p1, p2, PixelGridLineColorBrush, 1f, null);
                            }

                            p1.Y = cellRect.Top + 0.5f;
                            p2.Y = cellRect.Bottom - 0.5f;
                            rangeMin = Math.Max(0, (int) Math.Floor(1f *
                                -cellRect.Left * cellRectUnscaled.Width / cellRect.Width));
                            rangeMax = Math.Min(cellRectUnscaled.Width + 1, (int) Math.Ceiling(1f *
                                (clientSize.Width - cellRect.Left) * cellRectUnscaled.Width / cellRect.Width));
                            for (var j = rangeMin; j < rangeMax; j++) {
                                var x = cellRect.Left + j * cellRect.Width / cellRectUnscaled.Width;
                                p1.X = p2.X = x + 0.5f;
                                pRenderTarget->DrawLine(p1, p2, PixelGridLineColorBrush, 1f, null);
                            }
                        }
                    }
                } finally {
                    ArrayPool<LoadState>.Shared.Return(bitmapLoaded);
                    ArrayPool<Exception?>.Shared.Return(bitmapExceptions);
                }

                DrawText(
                    Control.AutoDescription,
                    overlayRect,
                    wordWrapping: WordWrapping.EmergencyBreak,
                    textAlignment: TextAlignment.Leading,
                    paragraphAlignment: ParagraphAlignment.Near,
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
            WordWrapping.EmergencyBreak,
            TextAlignment.Center,
            ParagraphAlignment.Center,
            ScalingFontTextFormat);
        var fontSizeScale = Control.EffectiveFontSizeScale;
        box = new(
            metrics.Left - 32 * fontSizeScale,
            metrics.Top - 32 * fontSizeScale,
            metrics.Left + metrics.Width + 32 * fontSizeScale,
            metrics.Top + metrics.Height + 32 * fontSizeScale);

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

        private TaskWrappingIUnknown<ID2D1Bitmap>?[ /* Image */][ /* Mip */][ /* Slice */]? _pBitmaps;

        public SourceSet(D2DTexRenderer renderer, Task<IBitmapSource> sourceTask) {
            _renderer = renderer;
            SourceTask = sourceTask;
            SourceTask.ContinueWith(r => {
                if (!r.IsCompletedSuccessfully)
                    return;

                var source = r.Result;
                _ = SafeDispose.EnumerableAsync(ref _pBitmaps);

                _pBitmaps = new TaskWrappingIUnknown<ID2D1Bitmap>[source.ImageCount][][];
                for (var i = 0; i < _pBitmaps.Length; i++) {
                    var a1 = _pBitmaps[i] = new TaskWrappingIUnknown<ID2D1Bitmap>[source.NumberOfMipmaps(i)][];
                    for (var j = 0; j < a1.Length; j++)
                        a1[j] = new TaskWrappingIUnknown<ID2D1Bitmap>[source.NumSlicesOfMipmap(i, j)];
                }

                r.Result.LayoutChanged += SourceTaskOnLayoutChanged;
                SourceTaskOnLayoutChanged();
            });
        }

        public void Dispose() {
            _cancellationTokenSource.Cancel();
            _ = SafeDispose.EnumerableAsync(ref _pBitmaps);
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
                x => _pBitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        public bool IsEveryVisibleSliceReadyForDrawing() =>
            SourceTask.IsCompletedSuccessfully &&
            SourceTask.Result.Layout.All(
                x => _pBitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        private LoadState TryGetBitmapWrapperTaskAt(
            GridLayoutCell cell,
            out TaskWrappingIUnknown<ID2D1Bitmap>? wrapperTask,
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

            if (task.IsCompleted && _pBitmaps is { } pBitmaps) {
                wrapperTask = pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice];
                if (wrapperTask is null) {
                    wrapperTask = pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice] = new(Task.Run(
                        () => {
                            var ptr = new ComPtr<ID2D1Bitmap>();
                            // do NOT merge into constructor, or it will do AddRef, which we do not want.

                            if (task.Result.ConvertPixelFormatIfDifferent(out var after,
                                    WicNet.WicPixelFormat.GUID_WICPixelFormat32bppPBGRA, false)) {
                                using (after)
                                    _renderer.GetOrCreateFromWicBitmap(ref ptr.Handle, after);
                            } else
                                _renderer.GetOrCreateFromWicBitmap(ref ptr.Handle, task.Result);

                            return ptr;
                        },
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

        public LoadState TryGetBitmapAt(
            GridLayoutCell cell,
            out ID2D1Bitmap* pBitmap,
            out Exception? exception) {
            var state = TryGetBitmapWrapperTaskAt(cell, out var task, out exception);
            pBitmap = state == LoadState.Loaded ? task!.Result : null;
            return state;
        }

        private void SourceTaskOnLayoutChanged() {
            var layout = SourceTask.Result.Layout;
            var bitmapSource = SourceTask.Result;

            var allTasks = layout
                .Select(cell => bitmapSource.GetWicBitmapSourceAsync(cell)
                    .ContinueWith(result => {
                        if (this != _renderer.SourceCurrent || layout != bitmapSource.Layout)
                            return new(Task.FromCanceled<ComPtr<ID2D1Bitmap>>(default));

                        if (!result.IsCompletedSuccessfully)
                            throw result.Exception!;

                        TryGetBitmapWrapperTaskAt(cell, out var wrapperTask, out var exception);
                        return wrapperTask ?? throw exception ?? throw new InvalidOperationException();
                    }, _cancellationTokenSource.Token)
                    .ContinueWith(x => x.Result.Task, _cancellationTokenSource.Token)
                    .Unwrap()
                )
                .ToArray();

            _ = Task.WhenAny(allTasks)
                .ContinueWith(_ => {
                    if (this == _renderer.SourceCurrent && layout == bitmapSource.Layout)
                        _renderer.AnyBitmapSourceSliceLoadAttemptFinished?.Invoke(SourceTask);
                }, _cancellationTokenSource.Token);


            _ = Task.WhenAll(allTasks)
                .ContinueWith(_ => {
                    if (this == _renderer.SourceCurrent && layout == bitmapSource.Layout)
                        _renderer.AllBitmapSourceSliceLoadAttemptFinished?.Invoke(SourceTask);
                }, _cancellationTokenSource.Token);
        }
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

        // ReSharper disable once UnusedMember.Local
        public ConfiguredTaskAwaitable<ComPtr<T>> ConfigureAwait(bool continueOnCapturedContext) =>
            Task.ConfigureAwait(continueOnCapturedContext);

        public void Dispose() => Task.ContinueWith(result => {
            if (result.IsCompletedSuccessfully)
                result.Result.Release();
        });

        public ValueTask DisposeAsync() => new(Task.ContinueWith(result => {
            if (result.IsCompletedSuccessfully)
                result.Result.Release();
            return ValueTask.CompletedTask;
        }));
    }
}
