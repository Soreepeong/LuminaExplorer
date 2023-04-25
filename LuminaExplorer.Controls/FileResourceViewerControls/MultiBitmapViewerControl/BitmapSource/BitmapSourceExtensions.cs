using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.GridLayout;
using LuminaExplorer.Core.Util;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;

public static class BitmapSourceExtensions {
    public static Task<WicBitmapSource> GetWicBitmapSourceAsync(this IBitmapSource source, GridLayoutCell cell) =>
        source.GetWicBitmapSourceAsync(cell.ImageIndex, cell.Mipmap, cell.Slice);

    public static bool HasWicBitmapSource(this IBitmapSource source, GridLayoutCell cell) =>
        source.HasWicBitmapSource(cell.ImageIndex, cell.Mipmap, cell.Slice);

    public static Task<Bitmap> GetGdipBitmapAsync(this IBitmapSource source, GridLayoutCell cell) =>
        source.GetGdipBitmapAsync(cell.ImageIndex, cell.Mipmap, cell.Slice);

    public static bool HasGdipBitmap(this IBitmapSource source, GridLayoutCell cell) =>
        source.HasGdipBitmap(cell.ImageIndex, cell.Mipmap, cell.Slice);

    // https://stackoverflow.com/questions/44177115/copying-from-and-to-clipboard-loses-image-transparency/46424800#46424800
    public static async Task<bool> SetClipboardImage(
        this IBitmapSource source,
        TaskScheduler taskScheduler,
        DataObject? data = null) {
        if (source.Layout.Count == 0)
            return false;

        data ??= new();

        Bitmap? bitmap = null;
        var disposeBitmap = false;
        
        Stream? pngStream = null;
        Stream? dibStream = null;
        Stream? dibv5Stream = null;
        try {
            if (source.Layout.Count == 1) {
                var le = source.Layout[0];
                bitmap = await source.GetGdipBitmapAsync(le);
                if (bitmap.Size != le.Size)
                    bitmap = null;
            }

            if (bitmap is null) {
                var bitmaps = await Task.WhenAll(source.Layout.Select(source.GetGdipBitmapAsync));

                disposeBitmap = true;
                bitmap = new(source.Layout.GridSize.Width, source.Layout.GridSize.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bitmap);
                g.Clear(Color.Transparent);
                foreach (var (cell, cellBitmap) in source.Layout.Zip(bitmaps)) {
                    var rc = source.Layout.RectOf(cell);
                    g.DrawImage(cellBitmap, rc);
                }
            }

            // 1. PNG as top priority
            pngStream = new MemoryStream();
            bitmap.Save(pngStream, ImageFormat.Png);
            data.SetData("PNG", false, pngStream);

            // 2. Format17(DIB)
            ConvertToDib(bitmap, true, ref dibv5Stream);
            data.SetData("Format17", false, dibv5Stream);

            // 3. DIB, with alpha channel smuggled in
            ConvertToDib(bitmap, false, ref dibStream);
            data.SetData(DataFormats.Dib, false, dibStream);

            // 4. Fallback; no alpha
            using var bitmapWithoutTransparency = new Bitmap(bitmap.Width, bitmap.Height);
            using (var g = Graphics.FromImage(bitmapWithoutTransparency)) {
                g.Clear(Color.White);
                g.DrawImage(bitmap, 0, 0);
            }

            data.SetData(DataFormats.Bitmap, true, bitmapWithoutTransparency);

            await Task.Factory.StartNew(() => {
                Clipboard.Clear();
                Clipboard.SetDataObject(data, true);
            }, default, TaskCreationOptions.None, taskScheduler);

            return true;
        } finally {
            if (disposeBitmap)
                await SafeDispose.OneAsync(ref bitmap);
            await SafeDispose.OneAsync(ref dibStream);
            await SafeDispose.OneAsync(ref dibv5Stream);
            await SafeDispose.OneAsync(ref pngStream);
        }
    }

    public static void ConvertToDib(Bitmap bitmap, bool v5, ref Stream? stream) {
        var lb = bitmap.LockBits(
            new(Point.Empty, bitmap.Size),
            ImageLockMode.ReadOnly,
            v5 ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppPArgb);
        try {
            var bitmapSize = lb.Stride * lb.Height;

            stream ??= new MemoryStream(new byte[
                (v5 ? Unsafe.SizeOf<BitmapV5Header>() : Unsafe.SizeOf<BitmapInfo>()) +
                bitmapSize
            ]);

            unsafe {
                if (v5) {
                    var h = new BitmapV5Header {
                        Size = sizeof(BitmapV5Header),
                        Width = bitmap.Width,
                        Height = bitmap.Height, // positive: bottom-up
                        Planes = 1,
                        BitCount = 32,
                        Compression = 0u, // RGB
                        SizeImage = (uint) bitmapSize,
                        RedMask = 0x00FF0000u,
                        GreenMask = 0x0000FF00u,
                        BlueMask = 0x000000FFu,
                        AlphaMask = 0xFF000000u,
                        CSType = 0x57696E20, // LCS_WINDOWS_COLOR_SPACE
                        Intent = 4, // LCS_GM_IMAGES
                    };
                    
                    stream.Write(new(&h, h.Size));
                } else {
                    var h = new BitmapInfo {
                        Header = new() {
                            Size = sizeof(BitmapHeader),
                            Width = bitmap.Width,
                            Height = bitmap.Height, // positive: bottom-up
                            Planes = 1,
                            BitCount = 32,
                            Compression = 3u, // BITFIELDS
                            SizeImage = (uint) bitmapSize,
                        },
                        RedMask = 0x00FF0000u,
                        GreenMask = 0x0000FF00u,
                        BlueMask = 0x000000FFu,
                    };

                    stream.Write(new(&h, sizeof(BitmapInfo)));
                }
            }

            unsafe {
                var bitmapData = new ReadOnlySpan<byte>((void*) lb.Scan0, lb.Height * lb.Stride);
                if (lb.Stride > 0) {
                    for (var i = (lb.Height - 1) * lb.Stride; i >= 0; i -= lb.Stride)
                        stream.Write(bitmapData.Slice(i, lb.Stride));
                } else {
                    for (var i = 0; i < bitmapData.Length; i += lb.Stride)
                        stream.Write(bitmapData.Slice(i, lb.Stride));
                }
            }
        } finally {
            bitmap.UnlockBits(lb);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapHeader {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public ushort ClrUsed;
        public ushort ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo {
        public BitmapHeader Header;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CieXyzTriple {
        public DirectN.CIEXYZ ciexyzRed;
        public DirectN.CIEXYZ ciexyzGreen;
        public DirectN.CIEXYZ ciexyzBlue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapV5Header {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint RedMask;
        public uint GreenMask;
        public uint BlueMask;
        public uint AlphaMask;
        public uint CSType;
        public CieXyzTriple Endpoints;
        public uint GammaRed;
        public uint GammaGreen;
        public uint GammaBlue;
        public uint Intent;
        public uint ProfileData;
        public uint ProfileSize;
        public uint Reserved;
    }
}
