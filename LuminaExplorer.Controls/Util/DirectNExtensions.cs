using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;

namespace LuminaExplorer.Controls.Util;

public static class DirectNExtensions {
    private static readonly Lazy<IComObject<IWICImagingFactory>> WicFactoryLazy = new(() => {
        using var co = new ComObject<WicImagingFactory>(new());
        return new ComObject<IWICImagingFactory>(co.As<IWICImagingFactory>());
    });

    public static IComObject<IWICImagingFactory> WicFactory => WicFactoryLazy.Value;

    public static IComObject<IWICBitmapSource> ToWicBitmapSource(this TexFile texFile, int mipIndex, int slice) {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {

            WicFactory.Object.CreateDecoder(WICConstants.GUID_ContainerFormatDds, 0, out var pDecoder).ThrowOnError();
            using var decoder = new ComObject<IWICBitmapDecoder>(pDecoder);

            using var istream = new StreamIStreamWrapper(new DdsFile(
                Path.GetFileName(texFile.FilePath.Path),
                texFile).CreateStream());
            decoder.Object.Initialize(istream, WICDecodeOptions.WICDecodeMetadataCacheOnLoad);

            decoder.As<IWICDdsDecoder>(true).GetFrame(0, (uint) mipIndex, (uint) slice, out var pFrame).ThrowOnError();
            return new ComObject<IWICBitmapSource>(pFrame);
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

        var formatGuid = format switch {
            TexFile.TextureFormat.L8 => WICConstants.GUID_WICPixelFormat8bppGray,
            TexFile.TextureFormat.A8 => WICConstants.GUID_WICPixelFormat8bppAlpha,
            TexFile.TextureFormat.B5G5R5A1 => WICConstants.GUID_WICPixelFormat16bppBGRA5551,
            TexFile.TextureFormat.B8G8R8A8 => WICConstants.GUID_WICPixelFormat32bppBGRA,
            TexFile.TextureFormat.B8G8R8X8 => WICConstants.GUID_WICPixelFormat32bppBGR,
            TexFile.TextureFormat.R16G16B16A16F => WICConstants.GUID_WICPixelFormat64bppRGBAHalf,
            TexFile.TextureFormat.R32G32B32A32F => WICConstants.GUID_WICPixelFormat128bppRGBAFloat,
            TexFile.TextureFormat.D16 => WICConstants.GUID_WICPixelFormat16bppGray,
            TexFile.TextureFormat.Shadow16 => WICConstants.GUID_WICPixelFormat16bppGray,
            _ => throw new NotSupportedException(),
        };

        WicFactory.Object.CreateBitmapFromMemory(
            (uint) texBuf.Width,
            (uint) texBuf.Height,
            ref formatGuid,
            (uint) (texBuf.Width * bpp / 8),
            texBuf.RawData.Length,
            texBuf.RawData,
            out var pBitmap).ThrowOnError();
        return new ComObject<IWICBitmapSource>(pBitmap);
    }

    
    
    public static void Test(this IComObject<IWICBitmapSource> wicBitmap, Stream target, Guid format) {
        using var wrapper = new StreamIStreamWrapper(target, true);

        WicFactory.Object.CreateEncoder(format, 0, out var pEncoder).ThrowOnError();
        using var encoder = new ComObject<IWICBitmapEncoder>(pEncoder);

        encoder.Object.Initialize(wrapper, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache).ThrowOnError();

        encoder.Object.CreateNewFrame(out var pEncode, out var pOptions).ThrowOnError();
        using var frameEncode = new ComObject<IWICBitmapFrameEncode>(pEncode);
        using var options = new ComObject<IPropertyBag2>(pOptions);

        wicBitmap.Object.GetSize(out var w, out var h).ThrowOnError();
        frameEncode.Object.SetSize(w, h).ThrowOnError();

        wicBitmap.Object.GetResolution(out var dpiX, out var dpiY).ThrowOnError();
        frameEncode.Object.SetResolution(dpiX, dpiY).ThrowOnError();
    }

    public static bool ConvertPixelFormatDifferent(
        this IComObject<IWICBitmapSource> before,
        out IComObject<IWICBitmapSource> after,
        Guid targetPixelFormat,
        bool disposeBeforeIfConverted = true) {
        if (before.GetPixelFormat() == targetPixelFormat) {
            after = before;
            return false;
        }
        WicFactory.Object.CreateFormatConverter(out var pConverter).ThrowOnError();
        try {
            pConverter.Initialize(
                    before.Object,
                    ref targetPixelFormat,
                    WICBitmapDitherType.WICBitmapDitherTypeNone,
                    null,
                    0f,
                    WICBitmapPaletteType.WICBitmapPaletteTypeCustom)
                .ThrowOnError();
            after = new ComObject<IWICBitmapSource>(pConverter);
            if (disposeBeforeIfConverted)
                before.Dispose();
        } catch (Exception) {
            Marshal.ReleaseComObject(pConverter);
            throw;
        }

        return true;
    }

    public static bool TryToGdipBitmap(
        this IComObject<IWICBitmapSource> wicBitmap,
        [MaybeNullWhen(false)] out Bitmap b,
        [MaybeNullWhen(true)] out Exception exception) {
        b = null!;
        try {
            wicBitmap.Object.GetSize(out var w, out var h).ThrowOnError();
            b = new((int) w, (int) h, PixelFormat.Format32bppArgb);
            var bd = b.LockBits(
                new(Point.Empty, b.Size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"
            var targetFormatGuid = WICConstants.GUID_WICPixelFormat32bppBGRA;

            if (wicBitmap.GetPixelFormat() == targetFormatGuid) {
                wicBitmap.As<IWICBitmapSourceWithPointerBuffer>()
                    .CopyPixels(0, (uint) bd.Stride, bd.Height * bd.Stride, bd.Scan0).ThrowOnError();
                
            } else {
                WicFactory.Object.CreateFormatConverter(out var pConverter).ThrowOnError();
                using var converter = new ComObject<IWICFormatConverter>(pConverter);
                converter.Object.Initialize(
                        wicBitmap.Object,
                        ref targetFormatGuid,
                        WICBitmapDitherType.WICBitmapDitherTypeNone,
                        null,
                        0f,
                        WICBitmapPaletteType.WICBitmapPaletteTypeCustom)
                    .ThrowOnError();
                
                converter.As<IWICBitmapSourceWithPointerBuffer>()
                    .CopyPixels(0, (uint) bd.Stride, bd.Height * bd.Stride, bd.Scan0).ThrowOnError();
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

    // Copy of IWICBitmapSource from DirectN since we want direct access to pbBuffer rather than byte aray
    [Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComImport]
    private interface IWICBitmapSourceWithPointerBuffer {
        [MethodImpl(MethodImplOptions.PreserveSig)]
        HRESULT GetSize(out uint puiWidth, out uint puiHeight);

        [MethodImpl(MethodImplOptions.PreserveSig)]
        HRESULT GetPixelFormat(out Guid pPixelFormat);

        [MethodImpl(MethodImplOptions.PreserveSig)]
        HRESULT GetResolution(out double pDpiX, out double pDpiY);

        [MethodImpl(MethodImplOptions.PreserveSig)]
        HRESULT CopyPalette(IWICPalette pIPalette);

        [MethodImpl(MethodImplOptions.PreserveSig)]
        HRESULT CopyPixels(IntPtr prc, uint cbStride, int cbBufferSize, nint pbBuffer);
    }
}
