using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using WicNet;

namespace LuminaExplorer.Controls.Util;

public static class WicNetExtensions {
    public static WicBitmapSource ToWicBitmapSource(this TexFile texFile, int mipIndex, int slice) {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {
            using var decoder = WICImagingFactory.CreateDecoderFromStream(
                new DdsFile(Path.GetFileName(texFile.FilePath.Path), texFile).CreateStream(),
                WicImagingComponent.CLSID_WICDdsDecoder);
            using var ddsDecoder = decoder.AsComObject<IWICDdsDecoder>();

            ddsDecoder.Object.GetFrame(0, (uint) mipIndex, (uint) slice, out var pFrame).ThrowOnError();
            return new(pFrame);
        }

        var texBuf = texFile.TextureBuffer.Filter(mip: mipIndex, z: slice);
        var format = texFile.Header.Format;
        if (format == TexFile.TextureFormat.B4G4R4A4) {
            format = TexFile.TextureFormat.B8G8R8A8;
            texBuf = texBuf.Filter(format: format);
        }

        var bpp = 1 << (
            (int) (format & TexFile.TextureFormat.BppMask) >>
            (int) TexFile.TextureFormat.BppShift);

        return new(WICImagingFactory.CreateBitmapFromMemory(
            texBuf.Width,
            texBuf.Height,
            format switch {
                TexFile.TextureFormat.L8 => WicPixelFormat.GUID_WICPixelFormat8bppGray,
                TexFile.TextureFormat.A8 => WicPixelFormat.GUID_WICPixelFormat8bppAlpha,
                TexFile.TextureFormat.B5G5R5A1 => WicPixelFormat.GUID_WICPixelFormat16bppBGRA5551,
                TexFile.TextureFormat.B8G8R8A8 => WicPixelFormat.GUID_WICPixelFormat32bppBGRA,
                TexFile.TextureFormat.B8G8R8X8 => WicPixelFormat.GUID_WICPixelFormat32bppBGR,
                TexFile.TextureFormat.R16G16B16A16F => WicPixelFormat.GUID_WICPixelFormat64bppRGBAHalf,
                TexFile.TextureFormat.R32G32B32A32F => WicPixelFormat.GUID_WICPixelFormat128bppRGBAFloat,
                TexFile.TextureFormat.D16 => WicPixelFormat.GUID_WICPixelFormat16bppGray,
                TexFile.TextureFormat.Shadow16 => WicPixelFormat.GUID_WICPixelFormat16bppGray,
                _ => throw new NotSupportedException(),
            },
            texBuf.Width * bpp / 8,
            texBuf.RawData).Object);
    }

    public static bool ConvertPixelFormatIfDifferent(
        this WicBitmapSource before,
        out WicBitmapSource after,
        Guid targetPixelFormat,
        bool disposeBeforeIfConverted = true) {
        
        if (before.PixelFormat == targetPixelFormat) {
            after = before;
            return false;
        }

        var pConverter = WICImagingFactory.WithFactory(x => {
            x.CreateFormatConverter(out var pConverterInner).ThrowOnError();
            return pConverterInner;
        });
        
        try {
            pConverter.Initialize(
                    before.ComObject.Object,
                    targetPixelFormat,
                    WICBitmapDitherType.WICBitmapDitherTypeNone,
                    null,
                    0f,
                    WICBitmapPaletteType.WICBitmapPaletteTypeCustom)
                .ThrowOnError();
            after = new(pConverter);
            if (disposeBeforeIfConverted)
                before.Dispose();
        } catch (Exception) {
            Marshal.ReleaseComObject(pConverter);
            throw;
        }

        return true;
    }

    public static WicBitmapSource ToWicBitmapSource(this DdsFile ddsFile, int imageIndex, int mipIndex, int slice) {
        // TODO
        throw new NotImplementedException();
    }

    public static bool TryToGdipBitmap(
        this WicBitmapSource wicBitmap,
        [MaybeNullWhen(false)] out Bitmap b,
        [MaybeNullWhen(true)] out Exception exception) {
        b = null!;
        try {
            b = new(wicBitmap.Width, wicBitmap.Height, PixelFormat.Format32bppArgb);
            var bd = b.LockBits(
                new(Point.Empty, b.Size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            
            // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"
            var targetFormatGuid = WicPixelFormat.GUID_WICPixelFormat32bppBGRA;

            if (ConvertPixelFormatIfDifferent(wicBitmap, out var t, targetFormatGuid, false)) {
                try {
                    t.CopyPixels(bd.Height * bd.Stride, bd.Scan0, bd.Stride);
                } finally {
                    t.Dispose();
                }
            } else {
                wicBitmap.CopyPixels(bd.Height * bd.Stride, bd.Scan0, bd.Stride);
            }
            
            b.UnlockBits(bd);
            exception = null;
            return true;
        } catch (Exception e) {
            SafeDispose.One(ref b);
            b = null;
            exception = e;
            return false;
        }
    }
}
