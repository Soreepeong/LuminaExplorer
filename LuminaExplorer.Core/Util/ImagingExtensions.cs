using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Tex.Buffers;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;
using TerraFX.Interop.Windows;
using PlatformId = Lumina.Data.Structs.PlatformId;
using static TerraFX.Interop.Windows.Windows;

namespace LuminaExplorer.Core.Util;

public static class ImagingExtensions {
    public static readonly IReadOnlySet<string> ThumbnailSupportedExtensions;
    public static readonly ComPtr<IWICImagingFactory> WicFactory;
    public static readonly ComPtr<IWICImagingFactory2> WicFactory2;

    static unsafe ImagingExtensions()
    {
        fixed (Guid* pclsidWicImagingFactory = &CLSID.CLSID_WICImagingFactory)
        fixed (Guid* piidWicImagingFactory = &IID.IID_IWICImagingFactory)
        fixed (Guid* pclsidWicImagingFactory2 = &CLSID.CLSID_WICImagingFactory2)
        fixed (Guid* piidWicImagingFactory2 = &IID.IID_IWICImagingFactory2)
        fixed (IWICImagingFactory** ppWicFactory = &WicFactory.GetPinnableReference())
        fixed (IWICImagingFactory2** ppWicFactory2 = &WicFactory2.GetPinnableReference()) {
            if (CoCreateInstance(
                    pclsidWicImagingFactory2,
                    null,
                    (uint) CLSCTX.CLSCTX_INPROC_SERVER,
                    piidWicImagingFactory2,
                    (void**) ppWicFactory2).SUCCEEDED) {
                WicFactory2.As(ref WicFactory).Ensure();
            } else {
                CoCreateInstance(
                    pclsidWicImagingFactory,
                    null,
                    (uint) CLSCTX.CLSCTX_INPROC_SERVER,
                    piidWicImagingFactory,
                    (void**) ppWicFactory).Ensure();
            }
        }

        var extensions = new SortedSet<string>([".tex", ".atex", ".dds"]);
        foreach (var ptr in new ComponentEnumerable<IWICBitmapCodecInfo>(WicFactory, WICComponentType.WICEncoder)) {
            foreach (var ext in GetFileExtensions(ptr)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                extensions.Add(ext.ToLowerInvariant());
            }
        }

        ThumbnailSupportedExtensions = extensions;
        return;

        static string GetFileExtensions(IWICBitmapCodecInfo* info)
        {
            uint cch;
            if (info->GetFileExtensions(0, null, &cch).FAILED)
                return "";
            var buf = stackalloc char[checked((int) cch + 1)];
            if (info->GetFileExtensions(cch + 1, buf, &cch).FAILED)
                return "";
            return new(buf, 0, (int) cch);
        }
    }

    public static unsafe void GetMetrics<T>(
        this ComPtr<T> source,
        out int width,
        out int height,
        out Guid pixelFormat)
        where T : unmanaged, IWICBitmapSource.Interface
    {
        uint w, h;
        source.Get()->GetSize(&w, &h).Ensure();
        width = (int) w;
        height = (int) h;
        fixed (Guid* ppf = &pixelFormat)
            source.Get()->GetPixelFormat(ppf).Ensure();
    }

    public static unsafe void Save<T>(
        this ComPtr<T> source,
        Stream stream,
        Guid containerFormat,
        bool srgb = false)
        where T : unmanaged, IWICBitmapSource.Interface
    {
        Guid sourcePixelFormat;
        source.Get()->GetPixelFormat(&sourcePixelFormat).Ensure();

        uint width, height;
        source.Get()->GetSize(&width, &height).Ensure();

        var accepted = false;
        var pixelFormat = sourcePixelFormat;
        foreach (var pfi in new ComponentEnumerable<IWICPixelFormatInfo>(
                     WicFactory,
                     WICComponentType.WICPixelFormat)) {
            Guid tmp;
            if (pfi.Get()->GetFormatGUID(&tmp).FAILED)
                continue;
            accepted = tmp == pixelFormat;
            if (accepted)
                break;
        }

        if (!accepted)
            pixelFormat = GUID.GUID_WICPixelFormat32bppBGRA;

        using var encoder = new ComPtr<IWICBitmapEncoder>();
        WicFactory.Get()->CreateEncoder(&containerFormat, null, encoder.GetAddressOf()).Ensure();

        using var ws = ManagedIStream.Create(stream, true);
        encoder.Get()->Initialize(ws.Get(), WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache).Ensure();

        using var encoderFrame = default(ComPtr<IWICBitmapFrameEncode>);
        using var propertyBag = default(ComPtr<IPropertyBag2>);
        encoder.Get()->CreateNewFrame(encoderFrame.GetAddressOf(), propertyBag.GetAddressOf()).Ensure();

        // Opt-in to the WIC2 support for writing 32-bit Windows BMP files with an alpha channel
        if (containerFormat == GUID.GUID_ContainerFormatBmp && !WicFactory2.IsEmpty())
            propertyBag.Get()->Write("EnableV5Header32bppBGRA", true).Ensure();

        // if (props is not null)
        // {
        //     foreach (var (name, untypedValue) in props)
        //         propertyBag.Get()->Write(name, untypedValue).Ensure();
        // }

        encoderFrame.Get()->Initialize(propertyBag).Ensure();

        using (var metaWriter = default(ComPtr<IWICMetadataQueryWriter>)) {
            if (encoderFrame.Get()->GetMetadataQueryWriter(metaWriter.GetAddressOf()).SUCCEEDED) {
                if (containerFormat == GUID.GUID_ContainerFormatPng) {
                    // Set sRGB chunk
                    if (srgb) {
                        _ = metaWriter.Get()->SetMetadataByName("/sRGB/RenderingIntent", (byte) 0);
                    } else {
                        // add gAMA chunk with gamma 1.0
                        // gama value * 100,000 -- i.e. gamma 1.0
                        _ = metaWriter.Get()->SetMetadataByName("/sRGB/RenderingIntent", 100000U);

                        // remove sRGB chunk which is added by default.
                        _ = metaWriter.Get()->RemoveMetadataByName("/sRGB/RenderingIntent");
                    }
                } else {
                    // Set EXIF Colorspace of sRGB
                    _ = metaWriter.Get()->SetMetadataByName("System.Image.ColorSpace", (ushort) 0);
                }
            }
        }

        using var outBitmapSource = source.ConvertPixelFormat(pixelFormat);
        encoderFrame.Get()->SetPixelFormat(&pixelFormat).Ensure();
        encoderFrame.Get()->SetSize(width, height).Ensure();
        encoderFrame.Get()->WriteSource(outBitmapSource.Get(), null).Ensure();
        encoderFrame.Get()->Commit().Ensure();
        encoder.Get()->Commit().Ensure();
    }

    public static unsafe ComPtr<IWICBitmapSource> ToWicBitmapSource(this TexFile texFile, int mipIndex, int slice)
    {
        if (texFile.Header.Format is
            TexFile.TextureFormat.BC1 or
            TexFile.TextureFormat.BC2 or
            TexFile.TextureFormat.BC3) {
            var data = texFile.ToDdsFileFollowGameDx11Conversion().Data;
            fixed (Guid* pGuidDds = &CLSID.CLSID_WICDdsDecoder)
            fixed (byte* pData = data) {
                using var stream = new ComPtr<IWICStream>();
                WicFactory.Get()->CreateStream(stream.GetAddressOf()).Ensure();
                stream.Get()->InitializeFromMemory(pData, (uint) data.Length).Ensure();

                using var decoder = new ComPtr<IWICBitmapDecoder>();
                WicFactory.Get()->CreateDecoderFromStream(
                    (IStream*) stream.Get(),
                    pGuidDds,
                    WICDecodeOptions.WICDecodeMetadataCacheOnDemand,
                    decoder.GetAddressOf());

                using var ddsDecoder = new ComPtr<IWICDdsDecoder>();
                decoder.As(&ddsDecoder).Ensure();
                using var frame = new ComPtr<IWICBitmapFrameDecode>();
                WICDdsParameters ddsp;
                ddsDecoder.Get()->GetParameters(&ddsp).Ensure();
                ddsDecoder.Get()->GetFrame(0, (uint) mipIndex, (uint) slice, frame.GetAddressOf()).Ensure();
                return new((IWICBitmapSource*) frame.Get());
            }
        }

        // if (texFile.Header.Format is TexFile.TextureFormat.BC5 or TexFile.TextureFormat.BC7) {
        //     using var device = new ComPtr<ID3D11Device>();
        //     using var context = new ComPtr<ID3D11DeviceContext>();
        //     D3D_FEATURE_LEVEL level;
        //     var featureLevels = stackalloc D3D_FEATURE_LEVEL[] {
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
        //         D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1
        //     };
        //     DirectX.D3D11CreateDevice(
        //         null,
        //         D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
        //         default,
        //         0,
        //         featureLevels,
        //         7,
        //         D3D11.D3D11_SDK_VERSION,
        //         device.GetAddressOf(),
        //         &level,
        //         context.GetAddressOf()).Ensure();
        // }

        var texBuf = texFile.TextureBuffer.Filter(mip: mipIndex, z: slice);
        var format = texFile.Header.Format;
        if (format == TexFile.TextureFormat.B4G4R4A4) {
            format = TexFile.TextureFormat.B8G8R8A8;
            texBuf = texBuf.Filter(format: format);
        }

        var bpp = 1 << (
            (int) (format & TexFile.TextureFormat.BppMask) >>
            (int) TexFile.TextureFormat.BppShift);

        var guidPixelFormat = format switch {
            TexFile.TextureFormat.L8 => GUID.GUID_WICPixelFormat8bppGray,
            TexFile.TextureFormat.A8 => GUID.GUID_WICPixelFormat8bppAlpha,
            TexFile.TextureFormat.B5G5R5A1 => GUID.GUID_WICPixelFormat16bppBGRA5551,
            TexFile.TextureFormat.B8G8R8A8 => GUID.GUID_WICPixelFormat32bppBGRA,
            TexFile.TextureFormat.B8G8R8X8 => GUID.GUID_WICPixelFormat32bppBGR,
            TexFile.TextureFormat.R16G16B16A16F => GUID.GUID_WICPixelFormat64bppRGBAHalf,
            TexFile.TextureFormat.R32G32B32A32F => GUID.GUID_WICPixelFormat128bppRGBAFloat,
            TexFile.TextureFormat.D16 => GUID.GUID_WICPixelFormat16bppGray,
            TexFile.TextureFormat.Shadow16 => GUID.GUID_WICPixelFormat16bppGray,
            _ => throw new NotSupportedException(),
        };
        fixed (byte* pData = texBuf.RawData) {
            using var bitmap = new ComPtr<IWICBitmap>();
            WicFactory.Get()->CreateBitmapFromMemory(
                checked((uint) texBuf.Width),
                checked((uint) texBuf.Height),
                &guidPixelFormat,
                checked((uint) (texBuf.Width * bpp / 8)),
                checked((uint) texBuf.RawData.Length),
                pData,
                bitmap.GetAddressOf()).Ensure();
            return new((IWICBitmapSource*) bitmap.Get());
        }
    }

    public static unsafe ComPtr<IWICBitmapSource> ConvertPixelFormat<T>(
        this ComPtr<T> source,
        Guid pixelFormat)
        where T : unmanaged, IWICBitmapSource.Interface
    {
        var result = new ComPtr<IWICBitmapSource>();
        WICConvertBitmapSource(
            &pixelFormat,
            (IWICBitmapSource*) source.Get(),
            result.GetAddressOf()).Ensure();
        return result;

        // Guid sourcePixelFormat;
        // source.Get()->GetPixelFormat(&sourcePixelFormat).Ensure();
        // if (sourcePixelFormat == pixelFormat)
        //     return new((IWICBitmapSource*) source.Get());
        //
        // using var converter = new ComPtr<IWICFormatConverter>();
        // WicFactory.Get()->CreateFormatConverter(converter.GetAddressOf()).Ensure();
        //
        // converter.Get()->Initialize(
        //         (IWICBitmapSource*) source.Get(),
        //         &pixelFormat,
        //         WICBitmapDitherType.WICBitmapDitherTypeNone,
        //         null,
        //         0f,
        //         WICBitmapPaletteType.WICBitmapPaletteTypeCustom)
        //     .Ensure();
        // return new((IWICBitmapSource*) converter.Get());
    }

    public static unsafe ComPtr<IWICBitmap> CreateBitmap(int width, int height, Guid pixelFormat)
    {
        var bitmap = new ComPtr<IWICBitmap>();
        WicFactory.Get()->CreateBitmap(
            checked((uint) width),
            checked((uint) height),
            &pixelFormat,
            WICBitmapCreateCacheOption.WICBitmapCacheOnDemand,
            bitmap.GetAddressOf()).Ensure();
        return bitmap;
    }

    public static unsafe ComPtr<IWICBitmap> AsBitmap<T>(this ComPtr<T> source, Guid desiredPixelFormat = default)
        where T : unmanaged, IWICBitmapSource.Interface
    {
        source.GetMetrics(out var width, out var height, out var pixelFormat);
        if (desiredPixelFormat != default && pixelFormat != desiredPixelFormat) {
            using var temp = source.ConvertPixelFormat(desiredPixelFormat);
            using var bitmap = CreateBitmap(width, height, pixelFormat);
            using var bitmapLock = bitmap.Lock(write: true);
            temp.Get()->CopyPixels(null, bitmapLock.Stride, bitmapLock.Length, bitmapLock.Data).Ensure();
            return new(bitmap);
        } else {
            var asisBitmap = new ComPtr<IWICBitmap>();
            if (source.As(ref asisBitmap).SUCCEEDED)
                return new(asisBitmap);

            using var bitmap = CreateBitmap(width, height, pixelFormat);
            using var bitmapLock = bitmap.Lock(write: true);
            source.Get()->CopyPixels(null, bitmapLock.Stride, bitmapLock.Length, bitmapLock.Data).Ensure();
            return new(bitmap);
        }
    }

    public static unsafe ComPtr<IWICBitmapSource> ToWicBitmapSource(
        this DdsFile ddsFile,
        int imageIndex,
        int mipIndex,
        int slice)
    {
        using var bitmap = CreateBitmap(
            ddsFile.Width(mipIndex),
            ddsFile.Height(mipIndex),
            GUID.GUID_WICPixelFormat32bppBGRA);
        using var bitmapLock = bitmap.Lock(write: true);

        ddsFile.PixFmt.ToB8G8R8A8(
            bitmapLock.Buffer,
            (int) bitmapLock.Stride,
            ddsFile.SliceOrFaceData(imageIndex, mipIndex, slice),
            ddsFile.Pitch(mipIndex),
            (int) bitmapLock.Width,
            (int) bitmapLock.Height);
        return new((IWICBitmapSource*) bitmap.Get());
    }

    public static unsafe bool TryToGdipBitmap<T>(
        this ComPtr<T> wicBitmap,
        [MaybeNullWhen(false)] out Bitmap b,
        [MaybeNullWhen(true)] out Exception exception)
        where T : unmanaged, IWICBitmapSource.Interface
    {
        try {
            uint width, height;
            wicBitmap.Get()->GetSize(&width, &height).Ensure();

            b = new(checked((int) width), checked((int) height), PixelFormat.Format32bppArgb);

            try {
                var bd = b.LockBits(new(Point.Empty, b.Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"
                var targetFormatGuid = GUID.GUID_WICPixelFormat32bppBGRA;

                try {
                    using (var t = wicBitmap.ConvertPixelFormat(targetFormatGuid)) {
                        t.Get()->CopyPixels(
                                null,
                                checked((uint) (bd.Height * bd.Stride)),
                                checked((uint) bd.Stride),
                                (byte*) bd.Scan0)
                            .Ensure();
                    }

                    exception = null;
                    return true;
                } catch {
                    b.UnlockBits(bd);
                    throw;
                }
            } catch {
                SafeDispose.One(ref b);
                throw;
            }
        } catch (Exception e) {
            b = null;
            exception = e;
            return false;
        }
    }

    public static unsafe bool TryToWicBitmap(
        this Bitmap source,
        out ComPtr<IWICBitmapSource> target,
        [MaybeNullWhen(true)] out Exception exception)
    {
        target = null!;
        BitmapData? lb = null;
        try {
            using var bitmap = new ComPtr<IWICBitmap>();
            fixed (Guid* pGuid = &GUID.GUID_WICPixelFormat32bppBGRA)
                WicFactory.Get()->CreateBitmap(
                    checked((uint) source.Width),
                    checked((uint) source.Height),
                    pGuid,
                    WICBitmapCreateCacheOption.WICBitmapCacheOnDemand,
                    bitmap.GetAddressOf()).Ensure();

            // WICPixelFormat says it's "BGRA"; Imaging.PixelFormat says it's "ARGB"
            lb = source.LockBits(
                new(Point.Empty, source.Size),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            var rc = new WICRect { X = 0, Y = 0, Width = source.Width, Height = source.Height };
            using var bitmapLock = new ComPtr<IWICBitmapLock>();
            bitmap.Get()->Lock(&rc, (uint) WICBitmapLockFlags.WICBitmapLockWrite, bitmapLock.GetAddressOf()).Ensure();

            uint bufferSize;
            byte* pData;
            bitmapLock.Get()->GetDataPointer(&bufferSize, &pData).Ensure();
            uint stride;
            bitmapLock.Get()->GetStride(&stride).Ensure();
            uint width, height;
            bitmapLock.Get()->GetSize(&width, &height).Ensure();

            if (lb.Stride != stride)
                throw new NotSupportedException("Stride does not match");
            if (lb.Height != height)
                throw new NotSupportedException("Height does not match");
            Unsafe.CopyBlock((void*) lb.Scan0, pData, stride * height);

            exception = null;
            return true;
        } catch (Exception e) {
            SafeDispose.One(ref target);
            target = null;
            exception = e;
            return false;
        } finally {
            if (lb is not null)
                source.UnlockBits(lb);
        }
    }

    public static async Task<Bitmap> ExtractMipmapOfSizeAtLeast(
        this Stream stream,
        int minEdgeLength,
        PlatformId platformId,
        CancellationToken cancellationToken = default)
    {
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

    public static unsafe Task<Bitmap> ExtractMipmapOfSizeAtLeastWithWic(
        this Stream stream,
        CancellationToken cancellationToken = default)
    {
        stream.Position = 0;

        using var mis = ManagedIStream.Create(stream, true);
        using var decoder = new ComPtr<IWICBitmapDecoder>();
        WicFactory.Get()->CreateDecoderFromStream(
            mis.Get(),
            null,
            WICDecodeOptions.WICDecodeMetadataCacheOnDemand,
            decoder.GetAddressOf()).Ensure();
        cancellationToken.ThrowIfCancellationRequested();

        using var frame = new ComPtr<IWICBitmapFrameDecode>();
        decoder.Get()->GetFrame(0, frame.GetAddressOf()).Ensure();
        cancellationToken.ThrowIfCancellationRequested();

        if (!frame.TryToGdipBitmap(out var b, out var ex))
            throw ex;

        return Task.FromResult(b);
    }

    public static async Task<Bitmap> ExtractMipmapOfSizeAtLeastForTex(
        this Stream stream,
        int minEdgeLength,
        PlatformId platformId,
        CancellationToken cancellationToken = default)
    {
        var s = new LuminaBinaryReader(stream, platformId).WithSeek(0);

        var header = stream switch {
            BufferedStream { UnderlyingStream: TextureSqpackFileStream utvfs } => utvfs.TexHeader,
            TextureSqpackFileStream tvfs => tvfs.TexHeader,
            _ => s.ReadStructure<TexFile.TexHeader>(),
        };

        cancellationToken.ThrowIfCancellationRequested();

        var level = 0;
        while (level < header.MipCount - 1 &&
               (header.Width >> (level + 1)) >= minEdgeLength &&
               (header.Height >> (level + 1)) >= minEdgeLength)
            level++;

        uint offset;
        int length;
        unsafe {
            offset = header.OffsetToSurface[level];
            length = (int) ((level == header.MipCount - 1
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
            [length],
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
        CancellationToken cancellationToken = default)
    {
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

    public static unsafe WICBitmapLock Lock<T>(
        this ComPtr<T> bitmap,
        bool read = false,
        bool write = false)
        where T : unmanaged, IWICBitmap.Interface => new(
        *(ComPtr<IWICBitmap>*) &bitmap,
        (read ? (uint) WICBitmapLockFlags.WICBitmapLockRead : 0u) |
        (write ? (uint) WICBitmapLockFlags.WICBitmapLockWrite : 0u)
    );

    public unsafe struct WICBitmapLock : IDisposable {
        public readonly Guid PixelFormat;
        public readonly byte* Data;
        public readonly uint Length;
        public readonly uint Width;
        public readonly uint Height;
        public readonly uint Stride;

        private ComPtr<IWICBitmapLock> _lock;

        public WICBitmapLock(ComPtr<IWICBitmap> bitmap, uint flags)
        {
            bitmap.GetMetrics(out var width, out var height, out this.PixelFormat);
            this.Width = (uint) width;
            this.Height = (uint) height;

            this._lock = new();
            var rc = new WICRect { X = 0, Y = 0, Width = width, Height = height };
            fixed (IWICBitmapLock** ppLock = &this._lock.GetPinnableReference())
                bitmap.Get()->Lock(&rc, flags, ppLock).Ensure();

            uint bufferSize;
            byte* pData;
            this._lock.Get()->GetDataPointer(&bufferSize, &pData).Ensure();
            this.Data = pData;
            this.Length = bufferSize;

            uint stride;
            this._lock.Get()->GetStride(&stride).Ensure();
            this.Stride = stride;
        }

        public Span<byte> Buffer => new(this.Data, (int) this.Length);

        public void Dispose() => this._lock.Dispose();
    }

    private readonly struct ComponentEnumerable<T> : IEnumerable<ComPtr<T>>
        where T : unmanaged, IWICComponentInfo.Interface {
        private readonly ComPtr<IWICImagingFactory> factory;
        private readonly WICComponentType componentType;

        /// <summary>Initializes a new instance of the <see cref="ComponentEnumerable{T}"/> struct.</summary>
        /// <param name="factory">The WIC factory. Ownership is not transferred.</param>
        /// <param name="componentType">The component type to enumerate.</param>
        public ComponentEnumerable(ComPtr<IWICImagingFactory> factory, WICComponentType componentType)
        {
            this.factory = factory;
            this.componentType = componentType;
        }

        public unsafe ManagedIEnumUnknownEnumerator<T> GetEnumerator()
        {
            var enumUnknown = default(ComPtr<IEnumUnknown>);
            this.factory.Get()->CreateComponentEnumerator(
                (uint) this.componentType,
                (uint) WICComponentEnumerateOptions.WICComponentEnumerateDefault,
                enumUnknown.GetAddressOf()).Ensure();
            return new(enumUnknown);
        }

        IEnumerator<ComPtr<T>> IEnumerable<ComPtr<T>>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
