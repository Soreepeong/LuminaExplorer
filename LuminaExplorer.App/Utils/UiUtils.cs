using System.Runtime.InteropServices;

namespace LuminaExplorer.App.Utils;

public static partial class UiUtils {
    private static readonly string[] SizeUnits = {"B", "KB", "MB", "GB", "TB"};

    public static string FormatSize(long size) {
        if (size == 0)
            return "0 B";

        foreach (var sizeUnit in SizeUnits.SkipLast(1)) {
            if (size < 1024)
                return $"{size:##,###} {sizeUnit}";
            size /= 1024;
        }

        return $"{size:##,###} {SizeUnits[^1]}";
    }

    public static string FormatSize(ulong size) {
        if (size == 0)
            return "0 B";

        foreach (var sizeUnit in SizeUnits.SkipLast(1)) {
            if (size < 1024)
                return $"{size:##,###} {sizeUnit}";
            size /= 1024;
        }

        return $"{size:##,###} {SizeUnits[^1]}";
    }

    public static Icon? ExtractPeIcon(string filePath, int index, bool largeIcon = true) {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        nint hIcon;
        if (largeIcon) {
            if (0 == ExtractIconExW(filePath, index, out hIcon, nint.Zero, 1))
                hIcon = 0;
        } else {
            if (0 == ExtractIconExW(filePath, index, nint.Zero, out hIcon, 1))
                hIcon = 0;
        }

        if (hIcon == 0)
            return null;

        try {
            // FromHandle does not take ownership of the provided handle.
            // Clone will make a deep copy, which will have its own copy and ownership. 
            return (Icon) Icon.FromHandle(hIcon).Clone();
        } finally {
            DestroyIcon(hIcon);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("shell32", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ExtractIconExW(string lpszFile, int nIconIndex, out nint phiconLarge, nint phiconSmall,
        int nIcons);

    [LibraryImport("shell32", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ExtractIconExW(string lpszFile, int nIconIndex, nint phiconLarge, out nint phiconSmall,
        int nIcons);
}
