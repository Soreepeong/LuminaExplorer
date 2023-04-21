using System.Runtime.InteropServices;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed class D2DRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
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

                try {
                    unsafe {
                        Marshal.ThrowExceptionForHR(RenderTarget.CreateSolidColorBrush(
                            new D3Dcolorvalue(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f),
                            null,
                            ref _borderColorBrush));
                    }
                } catch (Exception e) {
                    LastException = e;
                }
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

        protected override void Dispose(bool disposing) {
            _borderColorBrush.Dispose();
            _borderColorBrush = null;
            _bitmap.Dispose();
            _bitmap = null;
        }

        public unsafe bool LoadTexFile(TexFile texFile, int mipIndex, int slice) {
            ComPtr<ID2D1Bitmap> newBitmap = null;

            try {
                using var wicBitmap = texFile.ToWicBitmap(mipIndex, slice);
                wicBitmap.ConvertTo(
                    WicNet.WicPixelFormat.GUID_WICPixelFormat32bppPBGRA,
                    paletteTranslate: DirectN.WICBitmapPaletteType.WICBitmapPaletteTypeMedianCut);
                Marshal.ThrowExceptionForHR(RenderTarget.CreateBitmapFromWicBitmap(
                    (IWICBitmapSource*) wicBitmap.ComObject.GetInterfacePointer<DirectN.IWICBitmapSource>(),
                    null,
                    ref newBitmap));

                (_bitmap, newBitmap) = (newBitmap, _bitmap);

                var ps = _bitmap.GetPixelSize();
                Size = new((int) ps.X, (int) ps.Y);
                return true;
            } catch (Exception e) {
                LastException = e;
                return false;
            } finally {
                newBitmap.Release();
            }
        }

        public void Reset() {
            _bitmap.Release();
            _bitmap = null;
            Size = new();
        }

        protected override void DrawInternal() {
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
                _bitmap,
                imageRect.ToSilkFloat(),
                1f, // opacity
                BitmapInterpolationMode.Linear,
                new Box2D<float>(0, 0, Size.Width, Size.Height));

            renderTarget.DrawRectangle(
                Rectangle.Inflate(imageRect, 1, 1).ToSilkFloat(),
                BorderColorBrush,
                1f, // stroke width
                new ComPtr<ID2D1StrokeStyle>());

            // TODO: draw text information
        }
    }
}
