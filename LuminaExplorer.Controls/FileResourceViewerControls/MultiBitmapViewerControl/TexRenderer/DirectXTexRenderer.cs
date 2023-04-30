using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;
using Silk.NET.DirectWrite;
using Silk.NET.Maths;
using Filter = Silk.NET.Direct3D11.Filter;
using FontStyle = Silk.NET.DirectWrite.FontStyle;
using IDWriteTextFormat = Silk.NET.DirectWrite.IDWriteTextFormat;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;

internal sealed unsafe class DirectXTexRenderer : DirectXRenderer<MultiBitmapViewerControl>, ITexRenderer {
    private readonly SourceSet?[] _sourceSets = new SourceSet?[2];

    private ID2D1Brush* _pForeColorWhenLoadedBrush;
    private ID2D1Brush* _pBackColorWhenLoadedBrush;
    private IDWriteTextFormat* _pScalingFontTextFormat;
    private ID3D11SamplerState* _pLinearSampler;
    private ID3D11SamplerState* _pPointSampler;

    private RectangleF? _autoDescriptionRectangle;

    private DirectXTexRendererShader? _tex2DShader;

    // ReSharper disable once IntroduceOptionalParameters.Global
    public DirectXTexRenderer(MultiBitmapViewerControl control) : this(control, null, null) { }

    public DirectXTexRenderer(
        MultiBitmapViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, false, pDevice, pDeviceContext) {
        Control.Resize += ControlOnResize;
        Control.FontSizeStepLevelChanged += ControlOnFontSizeStepLevelChanged;
        Control.ForeColorWhenLoadedChanged += ControlOnForeColorWhenLoadedChanged;
        Control.BackColorWhenLoadedChanged += ControlOnBackColorWhenLoadedChanged;

        _tex2DShader = new(Device);
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            Control.Resize -= ControlOnResize;
            Control.FontSizeStepLevelChanged -= ControlOnFontSizeStepLevelChanged;
            Control.ForeColorWhenLoadedChanged -= ControlOnForeColorWhenLoadedChanged;
            Control.BackColorWhenLoadedChanged -= ControlOnBackColorWhenLoadedChanged;

            UpdateBitmapSource(null, null);
            SafeDispose.One(ref _tex2DShader);
        }

        SafeRelease(ref _pForeColorWhenLoadedBrush);
        SafeRelease(ref _pBackColorWhenLoadedBrush);
        SafeRelease(ref _pScalingFontTextFormat);
        SafeRelease(ref _pLinearSampler);
        SafeRelease(ref _pPointSampler);

        base.Dispose(disposing);
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceLoadAttemptFinished;

    public event Action<Task<IBitmapSource>>? AllBitmapSourceSliceLoadAttemptFinished;

    private SourceSet? SourcePrevious {
        get => _sourceSets[0];
        set => _sourceSets[0] = value;
    }

    private SourceSet? SourceCurrent {
        get => _sourceSets[1];
        set => _sourceSets[1] = value;
    }

    public Task<IBitmapSource>? PreviousSourceTask {
        get => SourcePrevious?.SourceTask;
        set => UpdateBitmapSource(value, CurrentSourceTask);
    }

    public Task<IBitmapSource>? CurrentSourceTask {
        get => SourceCurrent?.SourceTask;
        set => UpdateBitmapSource(PreviousSourceTask, value);
    }

    private ID2D1Brush* BackColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _pBackColorWhenLoadedBrush, Control.BackColorWhenLoaded);

    private ID2D1Brush* ForeColorWhenLoadedBrush =>
        GetOrCreateSolidColorBrush(ref _pForeColorWhenLoadedBrush, Control.ForeColorWhenLoaded);

    private IDWriteTextFormat* ScalingFontTextFormat {
        get {
            if (_pScalingFontTextFormat is null) {
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

                _autoDescriptionRectangle = null;
            }

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

    private ID3D11SamplerState* LinearSampler {
        get {
            if (_pLinearSampler is null) {
                var samplerDesc = new SamplerDesc {
                    Filter = Filter.MinMagMipLinear,
                    MaxAnisotropy = 0,
                    AddressU = TextureAddressMode.Wrap,
                    AddressV = TextureAddressMode.Wrap,
                    AddressW = TextureAddressMode.Wrap,
                    MipLODBias = 0f,
                    MinLOD = 0,
                    MaxLOD = float.MaxValue,
                    ComparisonFunc = ComparisonFunc.Never,
                };
                fixed (ID3D11SamplerState** pState = &_pLinearSampler)
                    ThrowH(Device->CreateSamplerState(&samplerDesc, pState));
            }

            return _pLinearSampler;
        }
    }

    private ID3D11SamplerState* PointSampler {
        get {
            if (_pPointSampler is null) {
                var samplerDesc = new SamplerDesc {
                    Filter = Filter.MinMagMipPoint,
                    MaxAnisotropy = 0,
                    AddressU = TextureAddressMode.Wrap,
                    AddressV = TextureAddressMode.Wrap,
                    AddressW = TextureAddressMode.Wrap,
                    MipLODBias = 0f,
                    MinLOD = 0,
                    MaxLOD = float.MaxValue,
                    ComparisonFunc = ComparisonFunc.Never,
                };
                fixed (ID3D11SamplerState** pState = &_pPointSampler)
                    ThrowH(Device->CreateSamplerState(&samplerDesc, pState));
            }

            return _pPointSampler;
        }
    }

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
        Span<float> colors = stackalloc float[4];
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

        DeviceContext->ClearRenderTargetView(pRenderTarget, ref colors[0]);

        if (_tex2DShader is null)
            return;

        var currentSourceFullyAvailable = SourceCurrent?.IsEveryVisibleSliceReadyForDrawing() is true;
        var zoom = Control.EffectiveZoom;
        foreach (var sourceSet in _sourceSets) {
            if (sourceSet is null)
                continue;
            if (currentSourceFullyAvailable && SourcePrevious == sourceSet) {
                Debug.Assert(SourcePrevious != SourceCurrent);
                continue;
            }

            if (sourceSet.SourceTask.IsCompletedSuccessfully) {
                var source = sourceSet.SourceTask.Result;

                foreach (var cell in source.Layout) {
                    if (!sourceSet.TryGetBitmapAt(
                            cell,
                            out _,
                            out var res,
                            out _))
                        continue;
                        
                    if (!sourceSet.TryGetCbuffer(cell, out var cbuffer))
                        continue;
                        
                    _tex2DShader.Draw(DeviceContext, res.ShaderResourceView, zoom >= 2 ? PointSampler : LinearSampler, cbuffer);
                }
            }
        }
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget) {
        var imageRect = Control.EffectiveRect;
        var clientSize = Control.ClientSize;
        var overlayRect = new RectangleF(
            Control.Padding.Left + Control.Margin.Left,
            Control.Padding.Top + Control.Margin.Top,
            clientSize.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
            clientSize.Height - Control.Padding.Vertical - Control.Margin.Vertical);

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

                foreach (var sliceCell in source.Layout) {
                    if (sourceSet.TryGetBitmapAt(
                            sliceCell,
                            out var state,
                            out _,
                            out var exc))
                        continue;

                    var isError = state == LoadState.Error;
                    string msg;
                    if (isError)
                        msg = $"Error\n({sliceCell.ImageIndex}, {sliceCell.Mipmap}, {sliceCell.Slice})\n{exc}";
                    else if (Control.IsLoadingBoxDelayed)
                        continue;
                    else
                        msg = "Loading...";

                    DrawText(
                        msg,
                        source.Layout.RectOf(sliceCell, imageRect),
                        wordWrapping: WordWrapping.NoWrap,
                        textAlignment: TextAlignment.Center,
                        paragraphAlignment: ParagraphAlignment.Center,
                        textFormat: ScalingFontTextFormat,
                        textBrush: ForeColorBrush,
                        shadowBrush: BackColorBrush,
                        opacity: 1f,
                        borderWidth: 2);
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

        try {
            BackColorBrush->SetOpacity(overlayBackOpacity);
            ForeColorBrush->SetOpacity(overlayForeOpacity);

            var box = new Box2D<float>(
                metrics.Left - 32 * fontSizeScale,
                metrics.Top - 32 * fontSizeScale,
                metrics.Left + metrics.Width + 32 * fontSizeScale,
                metrics.Top + metrics.Height + 32 * fontSizeScale);
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

    private void ControlOnFontSizeStepLevelChanged(object? sender, EventArgs e) =>
        SafeRelease(ref _pScalingFontTextFormat);

    private void ControlOnResize(object? sender, EventArgs e) =>
        SafeRelease(ref _pScalingFontTextFormat);

    private void ControlOnForeColorWhenLoadedChanged(object? sender, EventArgs e) =>
        SafeRelease(ref _pForeColorWhenLoadedBrush);

    private void ControlOnBackColorWhenLoadedChanged(object? sender, EventArgs e) =>
        SafeRelease(ref _pBackColorWhenLoadedBrush);

    private sealed class SourceSet : IDisposable, IAsyncDisposable {
        private readonly DirectXTexRenderer _renderer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        public readonly Task<IBitmapSource> SourceTask;

        private ResultDisposingTask<Texture2DShaderResource>?[ /* Image */][ /* Mip */][ /* Slice */]? _pBitmaps;
        private ConstantBufferResource<DirectXTexRendererShader.Cbuffer>?[]? _cbuffer;
        
        public SourceSet(DirectXTexRenderer renderer, Task<IBitmapSource> sourceTask) {
            _renderer = renderer;
            _renderer.Control.UseAlphaChannelChanged += MarkAllCbuffersChanged;
            _renderer.Control.VisibleColorChannelChanged += MarkAllCbuffersChanged;
            _renderer.Control.RotationChanged += MarkAllCbuffersChanged;
            _renderer.Control.ViewportChanged += MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellColor1Changed += MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellColor2Changed += MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellSizeChanged += MarkAllCbuffersChanged;
            _renderer.Control.PixelGridLineColorChanged += MarkAllCbuffersChanged;
            _renderer.Control.PixelGridMinimumZoomChanged += MarkAllCbuffersChanged;
            SourceTask = sourceTask;
            SourceTask.ContinueWith(r => {
                if (!r.IsCompletedSuccessfully)
                    return;

                renderer.Control.Invoke(() => {
                    var source = r.Result;
                    _ = SafeDispose.EnumerableAsync(ref _pBitmaps);

                    _pBitmaps = new ResultDisposingTask<Texture2DShaderResource>[source.ImageCount][][];
                    for (var i = 0; i < _pBitmaps.Length; i++) {
                        var a1 = _pBitmaps[i] =
                            new ResultDisposingTask<Texture2DShaderResource>[source.NumberOfMipmaps(i)][];
                        for (var j = 0; j < a1.Length; j++)
                            a1[j] = new ResultDisposingTask<Texture2DShaderResource>[source.NumSlicesOfMipmap(i, j)];
                    }

                    r.Result.LayoutChanged += SourceTaskOnLayoutChanged;
                    SourceTaskOnLayoutChanged();
                });
            });
        }

        public void Dispose() {
            _renderer.Control.UseAlphaChannelChanged -= MarkAllCbuffersChanged;
            _renderer.Control.VisibleColorChannelChanged -= MarkAllCbuffersChanged;
            _renderer.Control.RotationChanged -= MarkAllCbuffersChanged;
            _renderer.Control.ViewportChanged -= MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellColor1Changed -= MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellColor2Changed -= MarkAllCbuffersChanged;
            _renderer.Control.TransparencyCellSizeChanged -= MarkAllCbuffersChanged;
            _renderer.Control.PixelGridLineColorChanged -= MarkAllCbuffersChanged;
            _renderer.Control.PixelGridMinimumZoomChanged -= MarkAllCbuffersChanged;
            _cancellationTokenSource.Cancel();
            _ = SafeDispose.EnumerableAsync(ref _pBitmaps);
            _ = SafeDispose.EnumerableAsync(ref _cbuffer);
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
            out ResultDisposingTask<Texture2DShaderResource>? wrapperTask,
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
                wrapperTask = pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice] ??= new(Task.Run(
                    () => Texture2DShaderResource.FromWicBitmap(_renderer.Device, task.Result),
                    _cancellationTokenSource.Token));

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
            [MaybeNullWhen(false)] out Texture2DShaderResource resource,
            [MaybeNullWhen(true)] out Exception exception) {
            state = TryGetBitmapWrapperTaskAt(cell, out var task, out exception);
            resource = state == LoadState.Loaded ? task!.Result : null;
            return resource is not null;
        }

        public bool TryGetCbuffer(
            GridLayoutCell cell,
            [MaybeNullWhen(false)] out ConstantBufferResource<DirectXTexRendererShader.Cbuffer> cbuffer) {
            cbuffer = null!;
            if (_cbuffer is null || cell.CellIndex >= _cbuffer.Length || cell.CellIndex < 0)
                return false;
            var layout = SourceTask.Result.Layout;
            var w = SourceTask.Result.WidthOfMipmap(cell.ImageIndex, cell.Mipmap);
            var h = SourceTask.Result.HeightOfMipmap(cell.ImageIndex, cell.Mipmap);
            cbuffer = _cbuffer[cell.CellIndex] ??= new(_renderer.Device, _renderer.DeviceContext, true);
            if (cbuffer.EnablePull) {
                var data = new DirectXTexRendererShader.Cbuffer {
                    Rotation = _renderer.Control.Rotation,
                    Pan = _renderer.Control.Pan,
                    EffectiveSize = _renderer.Control.EffectiveSize,
                    ClientSize = _renderer.Control.ClientSize,
                    CellRectScale = layout.ScaleOf(cell),
                    TransparencyCellColor1 = _renderer.Control.TransparencyCellSize > 0
                        ? _renderer.Control.TransparencyCellColor1.ToD3Dcolorvalue()
                        : new(0, 0, 0, 1),
                    TransparencyCellColor2 = _renderer.Control.TransparencyCellSize > 0
                        ? _renderer.Control.TransparencyCellColor2.ToD3Dcolorvalue()
                        : new(0, 0, 0, 1),
                    TransparencyCellSize = _renderer.Control.TransparencyCellSize > 0
                        ? _renderer.Control.TransparencyCellSize
                        : 1, // Prevent division by zero
                    PixelGridColor = _renderer.Control.PixelGridMinimumZoom <= _renderer.Control.EffectiveZoom
                        ? _renderer.Control.PixelGridLineColor.ToD3Dcolorvalue()
                        : new(0, 0, 0, 0),
                    CellSourceSize = new(w, h),
                    ChannelFilter = _renderer.Control.ChannelFilter,
                    UseAlphaChannel = _renderer.Control.UseAlphaChannel,
                };
                cbuffer.UpdateData(data);
            }

            return true;
        }

        private void MarkAllCbuffersChanged(object? sender, EventArgs e) {
            foreach (var c in _cbuffer ?? Array.Empty<ConstantBufferResource<DirectXTexRendererShader.Cbuffer>?>())
                if (c is not null)
                    c.EnablePull=true;
        }

        private void SourceTaskOnLayoutChanged() => _renderer.Control.Invoke(() => {
            var layout = SourceTask.Result.Layout;
            var bitmapSource = SourceTask.Result;
            _ = SafeDispose.EnumerableAsync(ref _cbuffer);

            _cbuffer = new ConstantBufferResource<DirectXTexRendererShader.Cbuffer>[layout.Count];

            var allTasks = layout
                .Select(cell => bitmapSource.GetWicBitmapSourceAsync(cell)
                    .ContinueWith(result => {
                        if (this != _renderer.SourceCurrent || layout != bitmapSource.Layout)
                            return new(Task.FromCanceled<Texture2DShaderResource>(default));

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
        });
    }
}
