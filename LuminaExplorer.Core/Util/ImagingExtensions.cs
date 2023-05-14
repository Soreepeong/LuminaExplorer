using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DirectN;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using Lumina.Data.Structs;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;
using WicNet;

namespace LuminaExplorer.Core.Util;

public static class ImagingExtensions {
    public static readonly IReadOnlySet<string> ThumbnailSupportedExtensions = WicImagingComponent
        .DecoderFileExtensions
        .Append(".tex")
        .Append(".atex")
        .Append(".dds")
        .ToImmutableHashSet();

    public static WicBitmapSource ToWicBitmapSource(this TexFile texFile, int mipIndex, int slice) {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {
            using var decoder = WICImagingFactory.CreateDecoderFromStream(
                texFile.ToDdsFileFollowGameDx11Conversion().CreateStream(),
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
        var bs = new WicBitmapSource(ddsFile.Width(mipIndex), ddsFile.Height(mipIndex),
            WicPixelFormat.GUID_WICPixelFormat32bppBGRA);
        try {
            bs.WithLock(WICBitmapLockFlags.WICBitmapLockWrite, wbl => {
                unsafe {
                    ddsFile.PixFmt.ToB8G8R8A8(
                        new((void*) wbl.DataPointer, (int) wbl.DataSize),
                        wbl.Stride,
                        ddsFile.SliceOrFaceData(imageIndex, mipIndex, slice),
                        ddsFile.Pitch(mipIndex),
                        wbl.Width,
                        wbl.Height);
                }
            });
            return bs;
        } catch (Exception) {
            bs.Dispose();
            throw;
        }
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

    public static bool TryToWicBitmap(this Bitmap bitmap,
        [MaybeNullWhen(false)] out WicBitmapSource b,
        [MaybeNullWhen(true)] out Exception exception) {
        b = null!;
        BitmapData? lb = null;
        try {
            b = new(bitmap.Width, bitmap.Height, WicPixelFormat.GUID_WICPixelFormat32bppBGRA);
            lb = bitmap.LockBits(
                new(Point.Empty, bitmap.Size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"

            b.WithLock(WICBitmapLockFlags.WICBitmapLockWrite, wlb => {
                if (lb.Stride != wlb.Stride)
                    throw new NotSupportedException("Stride does not match");
                if (lb.Height != wlb.Height)
                    throw new NotSupportedException("Height does not match");
                unsafe {
                    Unsafe.CopyBlock((void*) lb.Scan0, (void*) wlb.DataPointer, (uint) (wlb.Stride * wlb.Height));
                }
            });

            exception = null;
            return true;
        } catch (Exception e) {
            SafeDispose.One(ref b);
            b = null;
            exception = e;
            return false;
        } finally {
            if (lb is not null)
                bitmap.UnlockBits(lb);
        }
    }

    public static async Task<Bitmap> ExtractMipmapOfSizeAtLeast(
        this Stream stream,
        int minEdgeLength,
        PlatformId platformId,
        CancellationToken cancellationToken = default) {
        if (stream is not BufferedStream)
            stream = new BufferedStream(stream);
        var s = new LuminaBinaryReader(stream).WithSeek(0);
        var sniff = s.ReadUInt32();
        if (sniff == DdsHeaderLegacy.MagicValue)
            return await ExtractMipmapOfSizeAtLeastForDds(stream, minEdgeLength, cancellationToken);
        
        try {
            return await ExtractMipmapOfSizeAtLeastWithWic(stream, cancellationToken);
        } catch (Exception) {
            return await ExtractMipmapOfSizeAtLeastForTex(stream, minEdgeLength, platformId, cancellationToken);
        }
    }

    public static Task<Bitmap> ExtractMipmapOfSizeAtLeastWithWic(
        this Stream stream,
        CancellationToken cancellationToken = default) {
        stream.Position = 0;
        
        using var decoder = WICImagingFactory.CreateDecoderFromStream(stream);
        cancellationToken.ThrowIfCancellationRequested();
        
        decoder.Object.GetFrame(0, out var pFrame).ThrowOnError();
        using var frame = new WicBitmapSource(pFrame);
        cancellationToken.ThrowIfCancellationRequested();
        
        if (!frame.TryToGdipBitmap(out var b, out var ex))
            throw ex;
        
        return Task.FromResult(b);
    }

    public static async Task<Bitmap> ExtractMipmapOfSizeAtLeastForTex(
        this Stream stream,
        int minEdgeLength,
        PlatformId platformId,
        CancellationToken cancellationToken = default) {
        var s = new LuminaBinaryReader(stream, platformId).WithSeek(0);

        var header = stream switch {
            BufferedStream {UnderlyingStream: TextureSqpackFileStream utvfs} => utvfs.TexHeader,
            TextureSqpackFileStream tvfs => tvfs.TexHeader,
            _ => s.ReadStructure<TexFile.TexHeader>(),
        };

        cancellationToken.ThrowIfCancellationRequested();

        var level = 0;
        while (level < header.MipLevels - 1 &&
               (header.Width >> (level + 1)) >= minEdgeLength &&
               (header.Height >> (level + 1)) >= minEdgeLength)
            level++;

        uint offset;
        int length;
        unsafe {
            offset = header.OffsetToSurface[level];
            length = (int) ((level == header.MipLevels - 1
                ? stream.Length
                : header.OffsetToSurface[level + 1]) - offset);
        }

        var buffer = new byte[length];

        await s.WithSeek(offset).BaseStream.ReadExactlyAsync(buffer, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var mipWidth = Math.Max(1, header.Width >> level);
        var mipHeight = Math.Max(1, header.Height >> level);
        var tbuf = TextureBuffer.FromTextureFormat(
            header.Type,
            header.Format,
            mipWidth,
            mipHeight,
            1,
            new[] {length},
            buffer,
            platformId).Filter(format: TexFile.TextureFormat.B8G8R8A8);

        var bmp = new Bitmap(tbuf.Width, tbuf.Height, PixelFormat.Format32bppArgb);
        try {
            var lb = bmp.LockBits(new(Point.Empty, bmp.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(tbuf.RawData, 0, lb.Scan0, lb.Stride * lb.Height);
            bmp.UnlockBits(lb);
            return bmp;
        } catch (Exception) {
            bmp.Dispose();
            throw;
        }
    }

    public static async Task<Bitmap> ExtractMipmapOfSizeAtLeastForDds(
        this Stream stream,
        int minEdgeLength,
        CancellationToken cancellationToken = default) {
        var s = new LuminaBinaryReader(stream).WithSeek(0);

        var legacyHeader = s.ReadStructure<DdsHeaderLegacy>();
        DdsHeaderDxt10? dxt10Header = null;
        if (legacyHeader.Header.PixelFormat.Flags.HasFlag(DdsPixelFormatFlags.FourCc) &&
            legacyHeader.Header.PixelFormat.FourCc == DdsFourCc.Dx10) {
            dxt10Header = s.ReadStructure<DdsHeaderDxt10>();
        }

        var ddsFile = new DdsFile("", legacyHeader, dxt10Header, null!);

        cancellationToken.ThrowIfCancellationRequested();

        var level = 0;
        while (level < ddsFile.NumMipmaps - 1 &&
               ddsFile.Width(level + 1) >= minEdgeLength &&
               ddsFile.Height(level + 1) >= minEdgeLength)
            level++;

        var offset = ddsFile.SliceOrFaceDataOffset(0, level, 0, out var length);
        var buffer = new byte[length];
        await s.WithSeek(offset).BaseStream.ReadExactlyAsync(buffer, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var bmp = new Bitmap(ddsFile.Width(level), ddsFile.Height(level), PixelFormat.Format32bppArgb);
        try {
            var lb = bmp.LockBits(new(Point.Empty, bmp.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            ddsFile.PixFmt.ToB8G8R8A8(
                lb.Scan0,
                lb.Height * lb.Stride,
                lb.Stride,
                buffer,
                ddsFile.Pitch(level),
                bmp.Width,
                bmp.Height);
            bmp.UnlockBits(lb);
            return bmp;
        } catch (Exception) {
            bmp.Dispose();
            throw;
        }
    }
}
