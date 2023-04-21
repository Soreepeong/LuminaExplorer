using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DirectWrite;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using IDWriteTextFormat = Silk.NET.Direct2D.IDWriteTextFormat;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed unsafe class D2DRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
        private WicNet.WicBitmapSource? _wicBitmap;
        private ID2D1Bitmap* _pBitmap;
        private ID2D1Brush* _pBorderColorBrush;
        private Color _borderColor;

        public D2DRenderer(TexFileViewerControl control) : base(control) {
            BorderColor = Color.LightGray;
        }

        public bool HasImage => _pBitmap is not null;

        public Size Size { get; private set; }

        public Color BorderColor {
            get => _borderColor;
            set {
                if (_borderColor == value)
                    return;
                _borderColor = value;
                SafeRelease(ref _pBorderColorBrush);
            }
        }

        private ID2D1Brush* BorderColorBrush => GetOrCreateSolidColorBrush(ref _pBorderColorBrush, BorderColor);

        private ID2D1Bitmap* Bitmap => GetOrCreateFromWicBitmap(ref _pBitmap, _wicBitmap);

        protected override void Dispose(bool disposing) {
            SafeRelease(ref _pBorderColorBrush);
            SafeRelease(ref _pBitmap);
            _wicBitmap?.Dispose();
            _wicBitmap = null;
        }

        public bool LoadTexFile(TexFile texFile, int mipIndex, int slice) {
            ComPtr<ID2D1Bitmap> newBitmap = null;

            WicNet.WicBitmapSource? wicBitmap = null;
            try {
                wicBitmap = texFile.ToWicBitmap(mipIndex, slice);
                wicBitmap.ConvertTo(
                    WicNet.WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                    paletteTranslate: DirectN.WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                Size = new(wicBitmap.Width, wicBitmap.Height);

                (_wicBitmap, wicBitmap) = (wicBitmap, _wicBitmap);
                SafeRelease(ref _pBitmap);

                return true;
            } catch (Exception e) {
                LastException = e;
                return false;
            } finally {
                newBitmap.Release();
                wicBitmap?.Dispose();
            }
        }

        public void Reset() {
            SafeRelease(ref _pBitmap);
            Size = new();
        }

        protected override void DrawInternal() {
            var pRenderTarget = RenderTarget;

            var box = Control.ClientRectangle.ToSilkFloat();
            pRenderTarget->FillRectangle(&box, BackColorBrush);

            var imageRect = Control.Viewport.EffectiveRect;
            var insetRect = new Rectangle(
                Control.Padding.Left,
                Control.Padding.Top,
                Control.Width - Control.Padding.Left - Control.Padding.Right,
                Control.Height - Control.Padding.Bottom - Control.Padding.Top);

            var cellSize = Control.TransparencyCellSize;
            if (cellSize > 0) {
                var controlSize = Control.Size;
                var c1 = false;
                for (var i = 0; i < controlSize.Width; i += cellSize) {
                    var c2 = c1;
                    c1 = !c1;
                    for (var j = 0; j < controlSize.Height; j += cellSize) {
                        if (c2) {
                            box = new(i, j, i + cellSize, j + cellSize);
                            pRenderTarget->FillRectangle(&box, BorderColorBrush);
                        }

                        c2 = !c2;
                    }
                }
            }

            pRenderTarget->DrawBitmap(
                Bitmap,
                imageRect.ToSilkFloat(),
                1f, // opacity
                BitmapInterpolationMode.Linear,
                new Box2D<float>(0, 0, Size.Width, Size.Height));

            box = Rectangle.Inflate(imageRect, 1, 1).ToSilkFloat();
            pRenderTarget->DrawRectangle(
                &box,
                BorderColorBrush,
                1f, // stroke width
                null);

            var pTextFormat = FontTextFormat;
            pTextFormat->SetTextAlignment(TextAlignment.Trailing);
            pTextFormat->SetParagraphAlignment(ParagraphAlignment.Far);

            var zoomText = $"Zoom {Control.Viewport.EffectiveZoom * 100:0.00}%";
            box = insetRect.ToSilkFloat();
            for (var i = -2; i <= 2; i++) {
                for (var j = -2; j <= 2; j++) {
                    if (i == 0 && j == 0)
                        continue;
                    box = (insetRect with {Width = insetRect.Width + i, Height = insetRect.Height + j}).ToSilkFloat();
                    fixed (char* v = zoomText.AsSpan())
                        pRenderTarget->DrawTextA(
                            v,
                            (uint) zoomText.Length,
                            (IDWriteTextFormat*) pTextFormat,
                            &box,
                            BackColorBrush,
                            DrawTextOptions.None,
                            DwriteMeasuringMode.GdiNatural);
                }
            }

            box = insetRect.ToSilkFloat();
            fixed (char* v = zoomText.AsSpan())
                pRenderTarget->DrawTextA(
                    v,
                    (uint) zoomText.Length,
                    (IDWriteTextFormat*) pTextFormat,
                    &box,
                    ForeColorBrush,
                    DrawTextOptions.None,
                    DwriteMeasuringMode.GdiNatural);
        }
    }
}
