using System.Text;
using Lumina.Data.Files;
using LuminaExplorer.Controls.Util;
using Silk.NET.Core.Native;
using Silk.NET.Direct2D;
using Silk.NET.DirectWrite;
using Silk.NET.Maths;
using Rectangle = System.Drawing.Rectangle;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private sealed unsafe class D2DRenderer : BaseD2DRenderer<TexFileViewerControl>, ITexRenderer {
        private WicNet.WicBitmapSource? _wicBitmap;
        private ID2D1Bitmap* _pBitmap;
        private ID2D1Brush* _pBorderColorBrush;
        private Color _borderColor;

        private string? _descriptionText;
        private float _descriptionTextSourceZoom;

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

        public float DescriptionOpacity { get; set; }

        private ID2D1Brush* BorderColorBrush => GetOrCreateSolidColorBrush(ref _pBorderColorBrush, BorderColor);

        private ID2D1Bitmap* Bitmap => GetOrCreateFromWicBitmap(ref _pBitmap, _wicBitmap);

        protected override void Dispose(bool disposing) {
            Reset();
            SafeRelease(ref _pBorderColorBrush);
            _wicBitmap?.Dispose();
            _wicBitmap = null;
        }

        private string DescriptionText {
            get {
                var effectiveZoom = Control.Viewport.EffectiveZoom;
                if (_descriptionText is not null && Equals(effectiveZoom, _descriptionTextSourceZoom))
                    return _descriptionText;
                if (Control.File is not { } file ||
                    Control.Tree is not { } tree ||
                    Control.FileResourceTyped is not { } texFile)
                    return "";

                _descriptionTextSourceZoom = effectiveZoom;

                var sb = new StringBuilder();
                sb.AppendLine(tree.GetFullPath(file));
                sb.Append(texFile.Header.Format).Append("; ")
                    .Append($"{texFile.Data.Length:##,###} Bytes");
                if (texFile.Header.MipLevels > 1)
                    sb.Append("; ").Append(texFile.Header.MipLevels).Append(" mipmaps");
                sb.AppendLine();
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                    sb.Append("1D: ").Append(texFile.Header.Width);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                    sb.Append("2D: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                    sb.Append("3D: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height)
                        .Append(" x ").Append(texFile.Header.Depth);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                    sb.Append("Cube: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height);
                if (!Equals(effectiveZoom, 1f))
                    sb.Append($" ({effectiveZoom * 100:0.00}%)");
                sb.AppendLine();
                foreach (var f in new[] {
                             TexFile.Attribute.DiscardPerFrame,
                             TexFile.Attribute.DiscardPerMap,
                             TexFile.Attribute.Managed,
                             TexFile.Attribute.UserManaged,
                             TexFile.Attribute.CpuRead,
                             TexFile.Attribute.LocationMain,
                             TexFile.Attribute.NoGpuRead,
                             TexFile.Attribute.AlignedSize,
                             TexFile.Attribute.EdgeCulling,
                             TexFile.Attribute.LocationOnion,
                             TexFile.Attribute.ReadWrite,
                             TexFile.Attribute.Immutable,
                             TexFile.Attribute.TextureRenderTarget,
                             TexFile.Attribute.TextureDepthStencil,
                             TexFile.Attribute.TextureSwizzle,
                             TexFile.Attribute.TextureNoTiled,
                             TexFile.Attribute.TextureNoSwizzle
                         })
                    if (texFile.Header.Type.HasFlag(f))
                        sb.Append("+ ").AppendLine(f.ToString());
                return _descriptionText = sb.ToString();
            }
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

                _descriptionText = null;

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
            _descriptionText = null;
        }

        protected override void DrawInternal() {
            var pRenderTarget = RenderTarget;

            var box = Control.ClientRectangle.ToSilkFloat();
            pRenderTarget->FillRectangle(&box, BackColorBrush);

            var imageRect = Control.Viewport.EffectiveRect;
            var overlayRect = new Rectangle(
                Control.Padding.Left + Control.Margin.Left,
                Control.Padding.Top + Control.Margin.Top,
                Control.Width - Control.Padding.Horizontal - Control.Margin.Horizontal,
                Control.Height - Control.Padding.Vertical - Control.Margin.Vertical);

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

            DrawContrastingText(
                DescriptionText,
                overlayRect,
                opacity: DescriptionOpacity,
                wordWrapping: WordWrapping.EmergencyBreak,
                textAlignment: TextAlignment.Leading,
                paragraphAlignment: ParagraphAlignment.Near
            );
        }
    }
}
