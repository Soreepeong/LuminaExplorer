using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;
using LuminaExplorer.Core.Util;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;

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
        Stream? dibStream = null;
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
                foreach (var (cell, cellBitmap) in source.Layout.Zip(bitmaps)) {
                    var rc = source.Layout.RectOf(cell);
                    g.DrawImage(cellBitmap, rc);
                }
            }

            using var bitmapWithoutTransparency = new Bitmap(bitmap.Width, bitmap.Height);
            using (var g = Graphics.FromImage(bitmapWithoutTransparency)) {
                g.Clear(Color.White);
                g.DrawImage(bitmap, 0, 0);
            }
            
            // As standard bitmap, without transparency support
            data.SetData(DataFormats.Bitmap, true, bitmapWithoutTransparency);

            // As PNG. Gimp will prefer this over the other two.
            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream, ImageFormat.Png);
            data.SetData("PNG", false, pngStream);

            // As DIB. This is (wrongly) accepted as ARGB by many applications.
            ConvertToDib(bitmap, ref dibStream);
            data.SetData(DataFormats.Dib, false, dibStream);

            // The 'copy=true' argument means the MemoryStreams can be safely disposed after the operation.
            await Task.Factory.StartNew(() => {
                Clipboard.Clear();
                Clipboard.SetDataObject(data, true);
            }, default, TaskCreationOptions.None, taskScheduler);

            return true;
        } finally {
            if (disposeBitmap)
                bitmap?.Dispose();
            await SafeDispose.OneAsync(ref dibStream);
        }
    }

    public static void ConvertToDib(Bitmap bitmap, ref Stream? stream) {
        const int bitmapInfoHeaderSize = 0x28;
        const int bitfieldsSize = 12;
        var bitmapSize = bitmap.Width * bitmap.Height * 4;

        stream ??= new MemoryStream(new byte[bitmapInfoHeaderSize + bitfieldsSize + bitmapSize]);
        var bw = new BinaryWriter(stream);
        
        // BITMAPINFOHEADER
        bw.Write((uint) bitmapInfoHeaderSize); // DWORD biSize;
        bw.Write((uint) bitmap.Width); // DWORD biWidth;
        bw.Write((uint) bitmap.Height); // DWORD biHeight;
        bw.Write((ushort) 1); // WORD biPlanes;
        bw.Write((ushort) 32); // WORD biBitCount;
        bw.Write((uint) 3); // BITMAPCOMPRESSION biCompression = BITMAPCOMPRESSION.BITFIELDS;
        bw.Write((uint) bitmapSize); // DWORD biSizeImage;
        bw.Write((uint) 0); // DWORD biXPelsPerMeter = 0;
        bw.Write((uint) 0); // DWORD biYPelsPerMeter = 0;
        bw.Write((uint) 0); // DWORD biClrUsed = 0;
        bw.Write((uint) 0); // DWORD biClrImportant = 0;

        // BITFIELDS
        bw.Write((uint)0x00FF0000);
        bw.Write((uint)0x0000FF00);
        bw.Write((uint)0x000000FF);

        var lb = bitmap.LockBits(new(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try {
            var rowBytes = lb.Width * lb.Stride;
            unsafe {
                var bitmapData = new ReadOnlySpan<byte>((void*) lb.Scan0, lb.Height * rowBytes);
                for (var i = (lb.Height - 1) * rowBytes; i >= 0; i -= rowBytes)
                    stream.Write(bitmapData.Slice(i, rowBytes));
            }
        } finally {
            bitmap.UnlockBits(lb);
        }
    }
}
