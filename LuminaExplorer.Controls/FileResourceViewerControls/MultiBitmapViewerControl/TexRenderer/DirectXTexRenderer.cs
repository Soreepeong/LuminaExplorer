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
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.TexRenderer;

internal sealed unsafe class DirectXTexRenderer : DirectXRenderer<MultiBitmapViewerControl>, ITexRenderer {
    private readonly SourceSet?[] _sourceSets = new SourceSet?[2];

    private ComPtr<ID2D1Brush> _pForeColorWhenLoadedBrush;
    private ComPtr<ID2D1Brush> _pBackColorWhenLoadedBrush;
    private ComPtr<IDWriteTextFormat> _pScalingFontTextFormat;
    private ComPtr<ID3D11SamplerState> _pLinearSampler;
    private ComPtr<ID3D11SamplerState> _pPointSampler;

    private RectangleF? _autoDescriptionRectangle;

    private DirectXTexRendererShader? _tex2DShader;

    // ReSharper disable once IntroduceOptionalParameters.Global
    public DirectXTexRenderer(MultiBitmapViewerControl control) : this(control, null, null)
    { }

    public DirectXTexRenderer(
        MultiBitmapViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, false, pDevice, pDeviceContext)
    {
        this.Control.Resize += this.ControlOnResize;
        this.Control.FontSizeStepLevelChanged += this.ControlOnFontSizeStepLevelChanged;
        this.Control.ForeColorWhenLoadedChanged += this.ControlOnForeColorWhenLoadedChanged;
        this.Control.BackColorWhenLoadedChanged += this.ControlOnBackColorWhenLoadedChanged;

        this._tex2DShader = new(this.Device);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this.Control.Resize -= this.ControlOnResize;
            this.Control.FontSizeStepLevelChanged -= this.ControlOnFontSizeStepLevelChanged;
            this.Control.ForeColorWhenLoadedChanged -= this.ControlOnForeColorWhenLoadedChanged;
            this.Control.BackColorWhenLoadedChanged -= this.ControlOnBackColorWhenLoadedChanged;

            this.UpdateBitmapSource(null, null);
            SafeDispose.One(ref this._tex2DShader);
        }

        this._pForeColorWhenLoadedBrush.Reset();
        this._pBackColorWhenLoadedBrush.Reset();
        this._pScalingFontTextFormat.Reset();
        this._pLinearSampler.Reset();
        this._pPointSampler.Reset();

        base.Dispose(disposing);
    }

    public event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceLoadAttemptFinished;

    public event Action<Task<IBitmapSource>>? AllBitmapSourceSliceLoadAttemptFinished;

    private SourceSet? SourcePrevious {
        get => this._sourceSets[0];
        set => this._sourceSets[0] = value;
    }

    private SourceSet? SourceCurrent {
        get => this._sourceSets[1];
        set => this._sourceSets[1] = value;
    }

    public Task<IBitmapSource>? PreviousSourceTask {
        get => this.SourcePrevious?.SourceTask;
        set => this.UpdateBitmapSource(value, this.CurrentSourceTask);
    }

    public Task<IBitmapSource>? CurrentSourceTask {
        get => this.SourceCurrent?.SourceTask;
        set => this.UpdateBitmapSource(this.PreviousSourceTask, value);
    }

    private ID2D1Brush* BackColorWhenLoadedBrush => this.GetOrCreateSolidColorBrush(
        ref this._pBackColorWhenLoadedBrush,
        this.Control.BackColorWhenLoaded);

    private ID2D1Brush* ForeColorWhenLoadedBrush => this.GetOrCreateSolidColorBrush(
        ref this._pForeColorWhenLoadedBrush,
        this.Control.ForeColorWhenLoaded);

    private IDWriteTextFormat* ScalingFontTextFormat {
        get {
            if (this._pScalingFontTextFormat.IsEmpty()) {
                fixed (char* pName = this.Control.Font.Name.AsSpan())
                fixed (char* pEmpty = "\0".AsSpan())
                fixed (IDWriteTextFormat** ppFontTextFormat = &this._pScalingFontTextFormat.GetPinnableReference())
                    DWriteFactory->CreateTextFormat(
                        pName,
                        null,
                        this.Control.Font.Bold
                            ? DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD
                            : DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                        this.Control.Font.Italic
                            ? DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC
                            : DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                        DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                        this.Control.EffectiveFontSizeInPoints * this.Control.DeviceDpi / 72,
                        pEmpty,
                        ppFontTextFormat).Ensure();

                this._autoDescriptionRectangle = null;
            }

            return this._pScalingFontTextFormat;
        }
    }

    public RectangleF? AutoDescriptionRectangle {
        get {
            if (this._autoDescriptionRectangle is not null)
                return this._autoDescriptionRectangle.Value;
            var padding = this.Control.Padding;
            var margin = this.Control.Margin;
            var rc = new RectangleF(
                padding.Left + margin.Left,
                padding.Top + margin.Top,
                this.Control.ClientSize.Width - padding.Horizontal - margin.Horizontal,
                this.Control.ClientSize.Height - padding.Vertical - margin.Vertical);

            var textLayout = this.LayoutText(
                out var metrics,
                this.Control.AutoDescription,
                rc,
                DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
                DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
                DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
                this.ScalingFontTextFormat);
            try {
                rc.Width = metrics.width;
                rc.Height = metrics.height;
                return this._autoDescriptionRectangle = rc;
            } finally {
                textLayout->Release();
            }
        }
        set => this._autoDescriptionRectangle = value;
    }

    private ID3D11SamplerState* LinearSampler {
        get {
            if (this._pLinearSampler.IsEmpty()) {
                var samplerDesc = new D3D11_SAMPLER_DESC(
                    filter: D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                    addressU: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    addressV: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    addressW: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    mipLODBias: 0,
                    maxAnisotropy: 0,
                    comparisonFunc: D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                    borderColor: null,
                    minLOD: 0,
                    maxLOD: float.MaxValue);
                fixed (ID3D11SamplerState** pState = &this._pLinearSampler.GetPinnableReference())
                    this.Device->CreateSamplerState(&samplerDesc, pState).Ensure();
            }

            return this._pLinearSampler;
        }
    }

    private ID3D11SamplerState* PointSampler {
        get {
            if (this._pPointSampler.IsEmpty()) {
                var samplerDesc = new D3D11_SAMPLER_DESC(
                    filter: D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_POINT,
                    addressU: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    addressV: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    addressW: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                    mipLODBias: 0,
                    maxAnisotropy: 0,
                    comparisonFunc: D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                    borderColor: null,
                    minLOD: 0,
                    maxLOD: float.MaxValue);
                fixed (ID3D11SamplerState** pState = &this._pPointSampler.GetPinnableReference())
                    this.Device->CreateSamplerState(&samplerDesc, pState).Ensure();
            }

            return this._pPointSampler;
        }
    }

    public bool UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current)
    {
        this.LastException = null;

        if (previous == current)
            previous = null;

        var changed = false;
        if (this.SourcePrevious?.SourceTask == current) {
            if (this.SourceCurrent?.SourceTask == previous) {
                // swap
                (this.SourceCurrent, this.SourcePrevious) = (this.SourcePrevious, this.SourceCurrent);
                return true;
            }

            // move from prev to current
            this.SourceCurrent?.Dispose();
            (this.SourceCurrent, this.SourcePrevious) = (this.SourcePrevious, null);
            changed = true;
        } else if (this.SourceCurrent?.SourceTask == previous) {
            // move from curr to prev
            this.SourcePrevious?.Dispose();
            (this.SourcePrevious, this.SourceCurrent) = (this.SourceCurrent, null);
            changed = true;
        }

        if (previous != this.SourcePrevious?.SourceTask) {
            this.SourcePrevious?.Dispose();
            this.SourcePrevious = previous is null ? null : new(this, previous);
            changed = true;
        }

        if (current != this.SourceCurrent?.SourceTask) {
            this.SourceCurrent?.Dispose();
            this.SourceCurrent = current is null ? null : new(this, current);
            changed = true;
        }

        return changed;
    }

    public bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask) =>
        bitmapSourceTask is not null && (
            (bitmapSourceTask == this.SourceCurrent?.SourceTask &&
                this.SourceCurrent.IsAnyVisibleSliceReadyForDrawing()) ||
            (bitmapSourceTask == this.SourcePrevious?.SourceTask &&
                this.SourcePrevious.IsAnyVisibleSliceReadyForDrawing()));

    public bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask) =>
        bitmapSourceTask is not null && (
            (bitmapSourceTask == this.SourceCurrent?.SourceTask &&
                this.SourceCurrent.IsEveryVisibleSliceReadyForDrawing()) ||
            (bitmapSourceTask == this.SourcePrevious?.SourceTask &&
                this.SourcePrevious.IsEveryVisibleSliceReadyForDrawing()));

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget)
    {
        var colors = stackalloc float[4];
        Color color;
        if (this.SourceCurrent?.IsAnyVisibleSliceReadyForDrawing() is true)
            color = this.Control.BackColorWhenLoaded;
        else if (this.SourceCurrent?.SourceTask.IsFaulted is true)
            color = this.Control.BackColor;
        else if (this.SourcePrevious?.IsAnyVisibleSliceReadyForDrawing() is true)
            color = this.Control.BackColorWhenLoaded;
        else
            color = this.Control.BackColor;

        colors[0] = 1f * color.R / 255;
        colors[1] = 1f * color.G / 255;
        colors[2] = 1f * color.B / 255;
        colors[3] = 1f * color.A / 255;

        this.DeviceContext->ClearRenderTargetView(pRenderTarget, colors);

        if (this._tex2DShader is null)
            return;

        var currentSourceFullyAvailable = this.SourceCurrent?.IsEveryVisibleSliceReadyForDrawing() is true;
        var zoom = this.Control.EffectiveZoom;
        foreach (var sourceSet in this._sourceSets) {
            if (sourceSet is null)
                continue;
            if (currentSourceFullyAvailable && this.SourcePrevious == sourceSet) {
                Debug.Assert(this.SourcePrevious != this.SourceCurrent);
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

                    this._tex2DShader.Draw(
                        this.DeviceContext,
                        res.ShaderResourceView,
                        zoom >= 2 ? this.PointSampler : this.LinearSampler,
                        cbuffer);
                }
            }
        }
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget)
    {
        var imageRect = this.Control.EffectiveRect;
        var clientSize = this.Control.ClientSize;
        var overlayRect = new RectangleF(
            this.Control.Padding.Left + this.Control.Margin.Left,
            this.Control.Padding.Top + this.Control.Margin.Top,
            clientSize.Width - this.Control.Padding.Horizontal - this.Control.Margin.Horizontal,
            clientSize.Height - this.Control.Padding.Vertical - this.Control.Margin.Vertical);

        if (!this.Control.TryGetEffectiveOverlayInformation(
                out var overlayString,
                out var overlayForeOpacity,
                out var overlayBackOpacity,
                out var hideIfNotLoading))
            overlayString = null;

        var currentSourceFullyAvailable = this.SourceCurrent?.IsEveryVisibleSliceReadyForDrawing() is true;
        var isLoading = false;
        foreach (var sourceSet in this._sourceSets) {
            if (sourceSet is null)
                continue;
            if (currentSourceFullyAvailable && this.SourcePrevious == sourceSet) {
                Debug.Assert(this.SourcePrevious != this.SourceCurrent);
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
                    else if (this.Control.IsLoadingBoxDelayed)
                        continue;
                    else
                        msg = "Loading...";

                    this.DrawText(
                        msg,
                        source.Layout.RectOf(sliceCell, imageRect),
                        wordWrapping: DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP,
                        textAlignment: DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
                        paragraphAlignment: DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
                        textFormat: this.ScalingFontTextFormat,
                        textBrush: this.ForeColorBrush,
                        shadowBrush: this.BackColorBrush,
                        opacity: 1f,
                        borderWidth: 2);
                }

                this.DrawText(
                    this.Control.AutoDescription,
                    overlayRect,
                    wordWrapping: DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
                    textAlignment: DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING,
                    paragraphAlignment: DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
                    textFormat: this.ScalingFontTextFormat,
                    textBrush: this.ForeColorWhenLoadedBrush,
                    shadowBrush: this.BackColorWhenLoadedBrush,
                    opacity: this.Control.AutoDescriptionOpacity,
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

        var textLayout = this.LayoutText(
            out var metrics,
            overlayString,
            this.Control.ClientRectangle,
            DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_EMERGENCY_BREAK,
            DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
            this.ScalingFontTextFormat);
        var fontSizeScale = this.Control.EffectiveFontSizeScale;

        try {
            this.BackColorBrush->SetOpacity(overlayBackOpacity);
            this.ForeColorBrush->SetOpacity(overlayForeOpacity);

            var box = new D2D_RECT_F(
                metrics.left - 32 * fontSizeScale,
                metrics.top - 32 * fontSizeScale,
                metrics.left + metrics.width + 32 * fontSizeScale,
                metrics.top + metrics.height + 32 * fontSizeScale);
            pRenderTarget->FillRectangle(&box, this.BackColorBrush);

            for (var i = -2; i <= 2; i++) {
                for (var j = -2; j <= 2; j++) {
                    if (i == 0 && j == 0)
                        continue;

                    pRenderTarget->DrawTextLayout(new(i, j), textLayout, this.BackColorBrush);
                }
            }

            pRenderTarget->DrawTextLayout(new(), textLayout, this.ForeColorBrush);
        } finally {
            SafeRelease(ref textLayout);
        }
    }

    private void ControlOnFontSizeStepLevelChanged(object? sender, EventArgs e) =>
        this._pScalingFontTextFormat.Reset();

    private void ControlOnResize(object? sender, EventArgs e) =>
        this._pScalingFontTextFormat.Reset();

    private void ControlOnForeColorWhenLoadedChanged(object? sender, EventArgs e) =>
        this._pForeColorWhenLoadedBrush.Reset();

    private void ControlOnBackColorWhenLoadedChanged(object? sender, EventArgs e) =>
        this._pBackColorWhenLoadedBrush.Reset();

    private sealed class SourceSet : IDisposable, IAsyncDisposable {
        private readonly DirectXTexRenderer _renderer;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        public readonly Task<IBitmapSource> SourceTask;

        private ResultDisposingTask<Texture2DShaderResource>?[ /* Image */][ /* Mip */][ /* Slice */]? _pBitmaps;
        private ConstantBufferResource<DirectXTexRendererShader.Cbuffer>?[]? _cbuffer;

        public SourceSet(DirectXTexRenderer renderer, Task<IBitmapSource> sourceTask)
        {
            this._renderer = renderer;
            this._renderer.Control.UseAlphaChannelChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.VisibleColorChannelChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.RotationChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.ViewportChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellColor1Changed += this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellColor2Changed += this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellSizeChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.PixelGridLineColorChanged += this.MarkAllCbuffersChanged;
            this._renderer.Control.PixelGridMinimumZoomChanged += this.MarkAllCbuffersChanged;
            this.SourceTask = sourceTask;
            this.SourceTask.ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully)
                        return;

                    renderer.Control.Invoke(
                        () => {
                            if (this._cancellationTokenSource.IsCancellationRequested)
                                return;

                            var source = r.Result;
                            _ = SafeDispose.EnumerableAsync(ref this._pBitmaps);

                            this._pBitmaps = new ResultDisposingTask<Texture2DShaderResource>[source.ImageCount][][];
                            for (var i = 0; i < this._pBitmaps.Length; i++) {
                                var a1 = this._pBitmaps[i] =
                                    new ResultDisposingTask<Texture2DShaderResource>[source.NumberOfMipmaps(i)][];
                                for (var j = 0; j < a1.Length; j++)
                                    a1[j] = new ResultDisposingTask<Texture2DShaderResource>[source.NumSlicesOfMipmap(
                                        i,
                                        j)];
                            }

                            r.Result.LayoutChanged += this.SourceTaskOnLayoutChanged;
                            this.SourceTaskOnLayoutChanged();
                        });
                });
        }

        public void Dispose()
        {
            this._renderer.Control.UseAlphaChannelChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.VisibleColorChannelChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.RotationChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.ViewportChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellColor1Changed -= this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellColor2Changed -= this.MarkAllCbuffersChanged;
            this._renderer.Control.TransparencyCellSizeChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.PixelGridLineColorChanged -= this.MarkAllCbuffersChanged;
            this._renderer.Control.PixelGridMinimumZoomChanged -= this.MarkAllCbuffersChanged;
            this._cancellationTokenSource.Cancel();
            _ = SafeDispose.EnumerableAsync(ref this._pBitmaps);
            _ = SafeDispose.EnumerableAsync(ref this._cbuffer);
            this.SourceTask.ContinueWith(
                r => {
                    if (r.IsCompletedSuccessfully)
                        r.Result.LayoutChanged -= this.SourceTaskOnLayoutChanged;
                });
        }

        public ValueTask DisposeAsync()
        {
            this._cancellationTokenSource.Cancel();
            return new(Task.Run(this.Dispose));
        }

        public bool IsAnyVisibleSliceReadyForDrawing() =>
            this.SourceTask.IsCompletedSuccessfully && this.SourceTask.Result.Layout.Any(
                x => this._pBitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        public bool IsEveryVisibleSliceReadyForDrawing() =>
            this.SourceTask.IsCompletedSuccessfully && this.SourceTask.Result.Layout.All(
                x => this._pBitmaps?[x.ImageIndex][x.Mipmap][x.Slice]?.IsCompletedSuccessfully is true);

        private LoadState TryGetBitmapWrapperTaskAt(
            GridLayoutCell cell,
            out ResultDisposingTask<Texture2DShaderResource>? wrapperTask,
            out Exception? exception)
        {
            wrapperTask = null;
            exception = null;

            if (this.SourceTask.IsCompletedSuccessfully is not true) {
                if (this.SourceTask.IsFaulted)
                    exception = this.SourceTask.Exception;
                return exception is null ? LoadState.Loading : LoadState.Error;
            }

            var source = this.SourceTask.Result;
            if (this._pBitmaps is not { } pBitmaps)
                return LoadState.Loading;

            ref var slot = ref pBitmaps[cell.ImageIndex][cell.Mipmap][cell.Slice];
            if (slot is null) {
                if (source.SupportsRawSlice(cell.ImageIndex, cell.Mipmap)) {
                    slot = new(
                        Task.Run(
                            () => this.CreateFromRawSliceOrWic(source, cell),
                            this._cancellationTokenSource.Token));
                } else {
                    var task = source.GetWicBitmapSourceAsync(cell);
                    if (task.IsFaulted) {
                        exception = task.Exception;
                        return LoadState.Error;
                    }

                    if (!task.IsCompleted)
                        return LoadState.Loading;

                    slot = new(
                        Task.Run(
                            () => Texture2DShaderResource.FromWicBitmap(this._renderer.Device, task.Result),
                            this._cancellationTokenSource.Token));
                }
            }

            wrapperTask = slot;
            if (wrapperTask.IsCompletedSuccessfully)
                return LoadState.Loaded;

            if (wrapperTask.IsFaulted) {
                exception = wrapperTask.Task.Exception;
                return LoadState.Error;
            }

            return LoadState.Loading;
        }

        private Texture2DShaderResource CreateFromRawSliceOrWic(IBitmapSource source, GridLayoutCell cell)
        {
            try {
                return Texture2DShaderResource.FromRawSlice(
                    this._renderer.Device,
                    source.GetRawSlice(cell.ImageIndex, cell.Mipmap, cell.Slice));
            } catch (Exception e) when (e is not ObjectDisposedException) {
                // The device may not support the format (e.g. B4G4R4A4 needs D3D 11.1); decode on the CPU instead.
                return Texture2DShaderResource.FromWicBitmap(
                    this._renderer.Device,
                    source.GetWicBitmapSourceAsync(cell).GetAwaiter().GetResult());
            }
        }

        public bool TryGetBitmapAt(
            GridLayoutCell cell,
            out LoadState state,
            [MaybeNullWhen(false)] out Texture2DShaderResource resource,
            [MaybeNullWhen(true)] out Exception exception)
        {
            state = this.TryGetBitmapWrapperTaskAt(cell, out var task, out exception);
            resource = state == LoadState.Loaded ? task!.Result : null;
            return resource is not null;
        }

        public bool TryGetCbuffer(
            GridLayoutCell cell,
            [MaybeNullWhen(false)] out ConstantBufferResource<DirectXTexRendererShader.Cbuffer> cbuffer)
        {
            cbuffer = null!;
            if (this._cbuffer is null || cell.CellIndex >= this._cbuffer.Length || cell.CellIndex < 0)
                return false;
            var layout = this.SourceTask.Result.Layout;
            var w = this.SourceTask.Result.WidthOfMipmap(cell.ImageIndex, cell.Mipmap);
            var h = this.SourceTask.Result.HeightOfMipmap(cell.ImageIndex, cell.Mipmap);
            cbuffer = this._cbuffer[cell.CellIndex] ??= new(this._renderer.Device, this._renderer.DeviceContext, true);
            if (cbuffer.EnablePull) {
                var data = new DirectXTexRendererShader.Cbuffer {
                    Rotation = this._renderer.Control.Rotation,
                    Pan = this._renderer.Control.Pan,
                    EffectiveSize = this._renderer.Control.EffectiveSize,
                    ClientSize = this._renderer.Control.ClientSize,
                    CellRectScale = layout.ScaleOf(cell),
                    TransparencyCellColor1 = this._renderer.Control.TransparencyCellSize > 0
                        ? this._renderer.Control.TransparencyCellColor1.ToDxgiColor()
                        : new(0, 0, 0, 1),
                    TransparencyCellColor2 = this._renderer.Control.TransparencyCellSize > 0
                        ? this._renderer.Control.TransparencyCellColor2.ToDxgiColor()
                        : new(0, 0, 0, 1),
                    TransparencyCellSize = this._renderer.Control.TransparencyCellSize > 0
                        ? this._renderer.Control.LogicalToDeviceUnits(this._renderer.Control.TransparencyCellSize)
                        : 1, // Prevent division by zero
                    PixelGridColor = this._renderer.Control.PixelGridMinimumZoom <= this._renderer.Control.EffectiveZoom
                        ? this._renderer.Control.PixelGridLineColor.ToDxgiColor()
                        : new(0, 0, 0, 0),
                    CellSourceSize = new(w, h),
                    ChannelFilter = this._renderer.Control.ChannelFilter,
                    UseAlphaChannel = this._renderer.Control.UseAlphaChannel,
                    ReplicateRedChannel =
                        this._pBitmaps?[cell.ImageIndex][cell.Mipmap][cell.Slice] is { IsCompletedSuccessfully: true } t &&
                        t.Result.ReplicateRedChannel
                            ? 1
                            : 0,
                };
                cbuffer.UpdateData(data);
            }

            return true;
        }

        private void MarkAllCbuffersChanged(object? sender, EventArgs e)
        {
            foreach (var c in this._cbuffer ?? [])
                if (c is not null)
                    c.EnablePull = true;
        }

        private void SourceTaskOnLayoutChanged() =>
            this._renderer.Control.Invoke(
                () => {
                    if (this._cancellationTokenSource.IsCancellationRequested)
                        return;

                    var layout = this.SourceTask.Result.Layout;
                    var bitmapSource = this.SourceTask.Result;
                    _ = SafeDispose.EnumerableAsync(ref this._cbuffer);

                    this._cbuffer = new ConstantBufferResource<DirectXTexRendererShader.Cbuffer>[layout.Count];

                    var allTasks = layout
                        .Select(
                            cell => (bitmapSource.SupportsRawSlice(cell.ImageIndex, cell.Mipmap)
                                    ? Task.CompletedTask
                                    : bitmapSource.GetWicBitmapSourceAsync(cell))
                                .ContinueWith(
                                    result => {
                                        if (this != this._renderer.SourceCurrent || layout != bitmapSource.Layout)
                                            return new(Task.FromCanceled<Texture2DShaderResource>(default));

                                        if (!result.IsCompletedSuccessfully)
                                            throw result.Exception!;

                                        this.TryGetBitmapWrapperTaskAt(cell, out var wrapperTask, out var exception);
                                        return wrapperTask ?? throw exception ?? throw new InvalidOperationException();
                                    },
                                    this._cancellationTokenSource.Token)
                                .ContinueWith(x => x.Result.Task, this._cancellationTokenSource.Token)
                                .Unwrap()
                        )
                        .ToArray();

                    _ = Task.WhenAny(allTasks)
                        .ContinueWith(
                            _ => {
                                if (this == this._renderer.SourceCurrent && layout == bitmapSource.Layout)
                                    this._renderer.AnyBitmapSourceSliceLoadAttemptFinished?.Invoke(this.SourceTask);
                            },
                            this._cancellationTokenSource.Token);


                    _ = Task.WhenAll(allTasks)
                        .ContinueWith(
                            _ => {
                                if (this == this._renderer.SourceCurrent && layout == bitmapSource.Layout)
                                    this._renderer.AllBitmapSourceSliceLoadAttemptFinished?.Invoke(this.SourceTask);
                            },
                            this._cancellationTokenSource.Token);
                });
    }
}
