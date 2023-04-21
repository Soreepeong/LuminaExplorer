using DirectN;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util.TexToDds;
using WicNet;

namespace LuminaExplorer.Controls.Util;

public static class WicNetExtensions {
    public static WicBitmapSource ToWicBitmap(this TexFile texFile, int mipIndex, int slice) {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {
            using var decoder = WICImagingFactory.CreateDecoderFromStream(
                new DdsFile(texFile).CreateStream(),
                WicImagingComponent.CLSID_WICDdsDecoder);
            using var ddsDecoder = decoder.AsComObject<IWICDdsDecoder>();

            ddsDecoder.Object.GetFrame(0, (uint) mipIndex, (uint) slice, out var pFrame).ThrowOnError();
            return new(pFrame);
        }

        var bpp = 1 << (
            (int) (texFile.Header.Format & TexFile.TextureFormat.BppMask) >>
            (int) TexFile.TextureFormat.BppShift);

        var texBuf = texFile.TextureBuffer.Filter(mip: mipIndex, z: slice);
        return new(WICImagingFactory.CreateBitmapFromMemory(
            texBuf.Width,
            texBuf.Height,
            texFile.Header.Format switch {
                TexFile.TextureFormat.L8 => WicPixelFormat.GUID_WICPixelFormat8bppGray,
                TexFile.TextureFormat.A8 => WicPixelFormat.GUID_WICPixelFormat8bppAlpha,
                TexFile.TextureFormat.B4G4R4A4 => WicPixelFormat.GUID_WICPixelFormat16bppBGRA5551,
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
}
