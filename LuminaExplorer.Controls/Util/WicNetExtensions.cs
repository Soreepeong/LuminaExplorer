using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;

namespace LuminaExplorer.Controls.Util;

public static class WicNetExtensions {
    private static readonly Lazy<IComObject<IWICImagingFactory>> WicFactoryLazy = new(() =>
        new ComObject<IWICImagingFactory>((IWICImagingFactory)new WicImagingFactory()));

    public static IComObject<IWICImagingFactory> WicFactory => WicFactoryLazy.Value;

    public static IComObject<IWICBitmapSource> ToWicBitmap(this TexFile texFile, int mipIndex, int slice) {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {
            
            using var decoder = WicFactory.Object.CreateDecoderFromStream(
                new DdsFile(Path.GetFileName(texFile.FilePath.Path), texFile).CreateStream(),
                WICConstants.CLSID_WICDdsDecoder);
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

    public static bool TryToGdipBitmap(
        this IComObject<IWICBitmapSource> wicBitmap,
        [MaybeNullWhen(false)] out Bitmap b,
        [MaybeNullWhen(true)] out Exception exception) {
        b = null!;
        try {
            b = new(wicBitmap.Width, wicBitmap.Height, PixelFormat.Format32bppArgb);
            var bd = b.LockBits(
                new(Point.Empty, b.Size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            wicBitmap.CopyPixels(bd.Height * bd.Stride, bd.Scan0, bd.Stride);
            // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"
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
