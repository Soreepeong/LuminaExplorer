using System.Runtime.InteropServices;
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
    private sealed class D2DRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
        private WicNet.WicBitmapSource? _wicBitmap;
        private ComPtr<ID2D1Bitmap> _bitmap;
        private ComPtr<ID2D1SolidColorBrush> _borderColorBrush;
        private Color _borderColor;

        public D2DRenderer(TexFileViewerControl control) : base(control) {
            BorderColor = Color.LightGray;
        }

        public unsafe bool HasImage => _bitmap.Handle is not null;

        public Size Size { get; private set; }

        public Color BorderColor {
            get => _borderColor;
            set {
                if (_borderColor == value)
                    return;
                _borderColor = value;
                _borderColorBrush.Dispose();
                _borderColorBrush = null;
            }
        }

        private unsafe ComPtr<ID2D1SolidColorBrush> BorderColorBrush {
            get {
                if (_borderColorBrush.Handle is null)
                    Marshal.ThrowExceptionForHR(RenderTarget.CreateSolidColorBrush(
                        new D3Dcolorvalue(
                            BorderColor.R / 255f,
                            BorderColor.G / 255f,
                            BorderColor.B / 255f,
                            BorderColor.A / 255f),
                        null,
                        ref _borderColorBrush));
                return _borderColorBrush;
            }
        }

        private unsafe ComPtr<ID2D1Bitmap> Bitmap {
            get {
                if (_bitmap.Handle is not null)
                    return _bitmap;
                if (_wicBitmap is null)
                    return new();

                ComPtr<ID2D1Bitmap> newBitmap = new();                
                Marshal.ThrowExceptionForHR(RenderTarget.CreateBitmapFromWicBitmap(
                    (IWICBitmapSource*) _wicBitmap.ComObject.GetInterfacePointer<DirectN.IWICBitmapSource>(),
                    null,
                    ref newBitmap));

                (_bitmap, newBitmap) = (newBitmap, _bitmap);
                newBitmap.Dispose();
                return _bitmap;
            }
        }

        protected override void Dispose(bool disposing) {
            _borderColorBrush.Dispose();
            _borderColorBrush = null;
            _wicBitmap?.Dispose();
            _wicBitmap = null;
            _bitmap.Dispose();
            _bitmap = null;
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
                _bitmap.Dispose();
                _bitmap = null;
                
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
            _bitmap.Release();
            _bitmap = null;
            Size = new();
        }

        protected override unsafe void DrawInternal() {
            var renderTarget = RenderTarget;

            renderTarget.FillRectangle(Control.ClientRectangle.ToSilkFloat(), BackColorBrush);

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
                            renderTarget.FillRectangle(
                                new Box2D<float>(i, j, i + cellSize, j + cellSize),
                                BorderColorBrush);
                        }

                        c2 = !c2;
                    }
                }
            }

            renderTarget.DrawBitmap(
                Bitmap,
                imageRect.ToSilkFloat(),
                1f, // opacity
                BitmapInterpolationMode.Linear,
                new Box2D<float>(0, 0, Size.Width, Size.Height));

            renderTarget.DrawRectangle(
                Rectangle.Inflate(imageRect, 1, 1).ToSilkFloat(),
                BorderColorBrush,
                1f, // stroke width
                new ComPtr<ID2D1StrokeStyle>());

            var format = FontTextFormat;
            format.SetTextAlignment(TextAlignment.Trailing);
            format.SetParagraphAlignment(ParagraphAlignment.Far);

            var zoomText = $"Zoom {Control.Viewport.EffectiveZoom * 100:0.00}%";
            var box = insetRect.ToSilkFloat();
            for (var i = -2; i <= 2; i++) {
                for (var j = -2; j <= 2; j++) {
                    if (i == 0 && j == 0)
                        continue;
                    box = (insetRect with {Width = insetRect.Width + i, Height = insetRect.Height + j}).ToSilkFloat();
                    renderTarget.DrawTextA(
                        zoomText.AsSpan(),
                        (uint) zoomText.Length,
                        (IDWriteTextFormat*) format.Handle,
                        &box,
                        (ID2D1Brush*) BackColorBrush.Handle,
                        DrawTextOptions.None,
                        DwriteMeasuringMode.GdiNatural);
                }
            }

            box = insetRect.ToSilkFloat();
            renderTarget.DrawTextA(
                zoomText.AsSpan(),
                (uint) zoomText.Length,
                (IDWriteTextFormat*) format.Handle,
                &box,
                (ID2D1Brush*) ForeColorBrush.Handle,
                DrawTextOptions.None,
                DwriteMeasuringMode.GdiNatural);
        }
    }
}
