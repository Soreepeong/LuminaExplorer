using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using Silk.NET.Direct2D;
using Silk.NET.DirectWrite;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed unsafe class D2DTexRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
        private WicNet.WicBitmapSource? _wicBitmap;
        private ID2D1Bitmap* _pBitmap;
        private ID2D1Brush* _pForeColorWhenLoadedBrush;
        private ID2D1Brush* _pBackColorWhenLoadedBrush;
        private ID2D1Brush* _pBorderColorBrush;
        private ID2D1Brush* _pTransparencyCellColor1Brush;
        private ID2D1Brush* _pTransparencyCellColor2Brush;

        private CancellationTokenSource? _loadCancellationTokenSource;

        public D2DTexRenderer(TexFileViewerControl control) : base(control) {
            Control.ForeColorWhenLoadedChanged += ControlOnForeColorWhenLoadedChanged;
            Control.BackColorWhenLoadedChanged += ControlOnBackColorWhenLoadedChanged;
            Control.BorderColorChanged += ControlOnBorderColorChanged;
            Control.TransparencyCellColor1Changed += ControlOnTransparencyCellColor1Changed;
            Control.TransparencyCellColor2Changed += ControlOnTransparencyCellColor2Changed;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                Control.ForeColorWhenLoadedChanged -= ControlOnForeColorWhenLoadedChanged;
                Control.BackColorWhenLoadedChanged -= ControlOnBackColorWhenLoadedChanged;
                Control.BorderColorChanged -= ControlOnBorderColorChanged;
                Control.TransparencyCellColor1Changed -= ControlOnTransparencyCellColor1Changed;
                Control.TransparencyCellColor2Changed -= ControlOnTransparencyCellColor2Changed;
            }

            Reset();
            SafeRelease(ref _pForeColorWhenLoadedBrush);
            SafeRelease(ref _pBackColorWhenLoadedBrush);
            SafeRelease(ref _pBorderColorBrush);
            SafeRelease(ref _pTransparencyCellColor1Brush);
            SafeRelease(ref _pTransparencyCellColor2Brush);
        }

        public bool HasNondisposedBitmap => _wicBitmap is not null;

        public Size ImageSize => _wicBitmap is null
            ? Size.Empty
            : new((int) _wicBitmap.Size.Width, (int) _wicBitmap.Size.Height);

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

        private ID2D1Bitmap* Bitmap => GetOrCreateFromWicBitmap(ref _pBitmap, _wicBitmap);

        public Task LoadTexFileAsync(TexFile texFile, int mipIndex, int slice) {
            // Currently in UI thread
            Reset(false);
            State = ITexRenderer.LoadState.Loading;

            var cts = _loadCancellationTokenSource = new();

            return Control.RunOnUiThreadAfter(Task.Run(() => {
                // Currently NOT in UI thread
                WicNet.WicBitmapSource? wicBitmap = null;
                try {
                    wicBitmap = texFile.ToWicBitmap(mipIndex, slice);
                    wicBitmap.ConvertTo(
                        WicNet.WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                        paletteTranslate: DirectN.WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                    return wicBitmap;
                } catch (Exception) {
                    wicBitmap?.Dispose();
                    throw;
                }
            }, cts.Token), r => {
                // Back in UI thread
                try {
                    cts.Token.ThrowIfCancellationRequested();

                    SafeDispose(ref _wicBitmap);
                    SafeRelease(ref _pBitmap);

                    if (r.IsCompletedSuccessfully) {
                        _wicBitmap = r.Result;
                        State = ITexRenderer.LoadState.Loaded;
                    } else {
                        LastException = r.Exception ?? new Exception("This exception should not happen");
                        State = ITexRenderer.LoadState.Error;

                        throw LastException;
                    }
                } catch (Exception) {
                    if (r.IsCompletedSuccessfully)
                        r.Result.Dispose();
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
                SafeDispose(ref _wicBitmap);
                SafeRelease(ref _pBitmap);
            }

            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = null;

            State = ITexRenderer.LoadState.Empty;
        }

        protected override void DrawInternal() {
            var pRenderTarget = RenderTarget;

            var imageRect = Control.Viewport.EffectiveRect;
            var clientSize = Control.ClientSize;
            var overlayRect = new Rectangle(
                Control.Padding.Left + Control.Margin.Left,
                Control.Padding.Top + Control.Margin.Top,
                clientSize.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
                clientSize.Height - Control.Padding.Vertical - Control.Margin.Vertical);

            var box = Control.ClientRectangle.ToSilkFloat();
            if (Bitmap is null) {
                BackColorBrush->SetOpacity(1f);
                pRenderTarget->FillRectangle(&box, BackColorBrush);
                return;
            }

            BackColorWhenLoadedBrush->SetOpacity(1f);
            pRenderTarget->FillRectangle(&box, BackColorWhenLoadedBrush);

            if (Control.ShouldDrawTransparencyGrid(Control.ClientRectangle,
                    out var multiplier,
                    out var minX,
                    out var minY,
                    out var dx,
                    out var dy)) {
                box.Min.Y = minY * multiplier + dy;
                box.Max.Y = box.Min.Y + multiplier;
                var yLim = Math.Min(imageRect.Bottom, clientSize.Height);
                var xLim = Math.Min(imageRect.Right, clientSize.Width);
                TransparencyCellColor1Brush->SetOpacity(1f);
                TransparencyCellColor2Brush->SetOpacity(1f);
                for (var y = minY;; y++) {
                    var height = Math.Min(multiplier, yLim - box.Min.Y);
                    if (height > 0)
                        box.Max.Y = box.Min.Y + height;
                    else
                        break;

                    box.Min.X = minX * multiplier + dx;
                    box.Max.X = box.Min.X + multiplier;
                    for (var x = minX;; x++) {
                        var width = Math.Min(multiplier, xLim - box.Min.X);
                        if (width > 0)
                            box.Max.X = box.Min.X + width;
                        else
                            break;

                        pRenderTarget->FillRectangle(
                            &box,
                            (x + y) % 2 == 0
                                ? TransparencyCellColor1Brush
                                : TransparencyCellColor2Brush);

                        box.Min.X += multiplier;
                    }

                    box.Min.Y += multiplier;
                }
            }

            pRenderTarget->DrawBitmap(
                Bitmap,
                imageRect.ToSilkFloat(),
                1f, // opacity
                Control.NearestNeighborMinimumZoom <= Control.Viewport.EffectiveZoom
                    ? BitmapInterpolationMode.NearestNeighbor
                    : BitmapInterpolationMode.Linear,
                null);

            if (Control.ContentBorderWidth > 0) {
                box = RectangleF.Inflate(imageRect, Control.ContentBorderWidth / 2f, Control.ContentBorderWidth / 2f)
                    .ToSilkFloat();
                ContentBorderColorBrush->SetOpacity(1f);
                pRenderTarget->DrawRectangle(&box, ContentBorderColorBrush, Control.ContentBorderWidth, null);
            }

            if (State is ITexRenderer.LoadState.Loading or ITexRenderer.LoadState.Empty) {
                box = Control.ClientRectangle.ToSilkFloat();
                BackColorBrush->SetOpacity(Control.LoadingBackgroundOverlayOpacity);
                pRenderTarget->FillRectangle(&box, BackColorBrush);

                DrawText(
                    Control.LoadingText,
                    overlayRect,
                    wordWrapping: WordWrapping.EmergencyBreak,
                    textAlignment: TextAlignment.Center,
                    paragraphAlignment: ParagraphAlignment.Center,
                    textBrush: ForeColorBrush,
                    shadowBrush: BackColorBrush,
                    borderWidth: 2);
            } else {
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
    }
}
