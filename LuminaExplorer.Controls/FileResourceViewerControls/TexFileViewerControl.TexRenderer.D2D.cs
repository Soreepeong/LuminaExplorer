using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct2D;
using Silk.NET.DirectWrite;
using Silk.NET.Maths;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed unsafe class D2DTexRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
        private WicNet.WicBitmapSource?[] _wicBitmaps = Array.Empty<WicNet.WicBitmapSource>();
        private ID2D1Bitmap*[] _pBitmaps = new ID2D1Bitmap*[0];
        private IGridLayout? _layout;
        private ID2D1Brush* _pForeColorWhenLoadedBrush;
        private ID2D1Brush* _pBackColorWhenLoadedBrush;
        private ID2D1Brush* _pBorderColorBrush;
        private ID2D1Brush* _pTransparencyCellColor1Brush;
        private ID2D1Brush* _pTransparencyCellColor2Brush;
        private ID2D1Brush* _pPixelGridLineColorBrush;

        private CancellationTokenSource? _loadCancellationTokenSource;

        public D2DTexRenderer(TexFileViewerControl control) : base(control) {
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

            Reset();
            SafeRelease(ref _pForeColorWhenLoadedBrush);
            SafeRelease(ref _pBackColorWhenLoadedBrush);
            SafeRelease(ref _pBorderColorBrush);
            SafeRelease(ref _pTransparencyCellColor1Brush);
            SafeRelease(ref _pTransparencyCellColor2Brush);
            SafeRelease(ref _pPixelGridLineColorBrush);

            base.Dispose(disposing);
        }

        public bool HasNondisposedBitmap => _wicBitmaps.Any() && _wicBitmaps.Any(x => x is not null);

        public Size ImageSize => _layout?.GridSize ?? Size.Empty;

        public ITexRenderer.LoadState State { get; private set; } = ITexRenderer.LoadState.Empty;

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

        private ID2D1Bitmap* GetBitmapAt(int slice) {
            if (slice >= _wicBitmaps.Length)
                throw new ArgumentOutOfRangeException(nameof(slice), slice, null);

            if (_pBitmaps.Length != _wicBitmaps.Length) {
                for (var i = 0; i < _pBitmaps.Length; i++)
                    SafeRelease(ref _pBitmaps[i]);

                _pBitmaps = new ID2D1Bitmap*[_wicBitmaps.Length];
            }

            return _pBitmaps[slice] = GetOrCreateFromWicBitmap(ref _pBitmaps[slice], _wicBitmaps[slice]);
        }

        public Task LoadFileAsync(int mipIndex) {
            if (Control.FileResourceTyped is { } texFile)
                return LoadTexFileAsync(texFile, mipIndex);
            if (Control.PhysicalFile is { } pfile) {
                // TODO
            }

            Reset(false);
            return Task.CompletedTask;
        }

        private Task LoadTexFileAsync(TexFile texFile, int mipIndex) {
            Reset(false);
            State = ITexRenderer.LoadState.Loading;

            var cts = _loadCancellationTokenSource = new();

            return Control.RunOnUiThreadAfter(
                Task.WhenAll(Enumerable
                    .Range(0, texFile.TextureBuffer.DepthOfMipmap(mipIndex))
                    .Select(i => Task.Run(() => {
                        cts.Token.ThrowIfCancellationRequested();
                        var wb = texFile.ToWicBitmap(mipIndex, i);
                        try {
                            cts.Token.ThrowIfCancellationRequested();
                            wb.ConvertTo(
                                WicNet.WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                                paletteTranslate: DirectN.WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                            return wb;
                        } catch (Exception) {
                            wb.Dispose();
                            throw;
                        }
                    }, cts.Token))),
                r => {
                    try {
                        cts.Token.ThrowIfCancellationRequested();

                        SafeDispose.Array(ref _wicBitmaps);
                        SafeReleaseArray(ref _pBitmaps);
                        _layout = null;

                        if (r.IsCompletedSuccessfully) {
                            _wicBitmaps = r.Result;
                            _layout = Control.CreateGridLayout(mipIndex);
                            State = ITexRenderer.LoadState.Loaded;
                        } else {
                            LastException = r.Exception ?? new Exception("This exception should not happen");
                            State = ITexRenderer.LoadState.Error;

                            throw LastException;
                        }
                    } catch (Exception) {
                        if (r.IsCompletedSuccessfully)
                            SafeDispose.Enumerable(r.Result);

                        throw;
                    } finally {
                        if (cts == _loadCancellationTokenSource)
                            _loadCancellationTokenSource = null;
                        cts.Dispose();
                    }
                });
        }

        public void Reset(bool disposeBitmap = true) {
            LastException = null;

            if (disposeBitmap) {
                SafeDispose.Array(ref _wicBitmaps);
                SafeReleaseArray(ref _pBitmaps);
                _layout = null;
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;

            State = ITexRenderer.LoadState.Empty;
        }

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
            if (_layout is not { } layout) {
                BackColorBrush->SetOpacity(1f);
                pRenderTarget->FillRectangle(&box, BackColorBrush);
                return;
            }

            BackColorWhenLoadedBrush->SetOpacity(1f);
            pRenderTarget->FillRectangle(&box, BackColorWhenLoadedBrush);

            if (Control.ContentBorderWidth > 0)
                ContentBorderColorBrush->SetOpacity(1f);

            // 1. Draw transparency grids
            for (var i = 0; i < _wicBitmaps.Length; i++) {
                var cellRect = layout.RectOf(i, imageRect);
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
                    for (var y = minY; ; y++) {
                        box.Min.Y = y * multiplier + dy;
                        box.Max.Y = box.Min.Y + Math.Min(multiplier, yLim - box.Min.Y);
                        if (box.Min.Y >= box.Max.Y)
                            break;

                        for (var x = minX; ; x++) {
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
                for (var i = 0; i < _wicBitmaps.Length; i++) {
                    box = RectangleF.Inflate(
                        layout.RectOf(i, imageRect),
                        Control.ContentBorderWidth / 2f,
                        Control.ContentBorderWidth / 2f).ToSilkFloat();
                    pRenderTarget->DrawRectangle(&box, ContentBorderColorBrush, Control.ContentBorderWidth, null);
                }
            }

            // 3. Draw bitmaps
            for (var i = 0; i < _wicBitmaps.Length; i++) {
                box = layout.RectOf(i, imageRect).ToSilkFloat();
                pRenderTarget->DrawBitmap(
                    GetBitmapAt(i),
                    &box,
                    1f, // opacity
                    Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                        ? BitmapInterpolationMode.NearestNeighbor
                        : BitmapInterpolationMode.Linear,
                    null);
            }

            // 4. Draw pixel grids
            if (Control.PixelGridMinimumZoom <= Control.Viewport.EffectiveZoom) {
                var p1 = new Vector2D<float>();
                var p2 = new Vector2D<float>();

                for (var i = 0; i < _wicBitmaps.Length; i++) {
                    var cellRectUnscaled = layout.RectOf(i);
                    var cellRect = layout.RectOf(i, imageRect);

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

            var overlayString = Control.OverlayString;
            var overlayOpacity = Control.OverlayOpacity;
            if (string.IsNullOrWhiteSpace(overlayString) || overlayOpacity == 0) {
                if (State is ITexRenderer.LoadState.Loading or ITexRenderer.LoadState.Empty) {
                    if (!Control.IsLoadingBoxDelayed) {
                        overlayString = Control.LoadingText;
                        overlayOpacity = Control.OverlayBackgroundOpacity;
                    }
                }
            }
            
            if (!string.IsNullOrWhiteSpace(overlayString) && overlayOpacity > 0) {
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
                    BackColorBrush->SetOpacity(overlayOpacity);
                    ForeColorBrush->SetOpacity(overlayOpacity);
                    
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
        }

        public override bool Draw(PaintEventArgs e) {
            if (base.Draw(e))
                return true;

            State = ITexRenderer.LoadState.Error;
            return false;
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
    }
}
