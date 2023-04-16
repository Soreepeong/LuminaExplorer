using System.Runtime.InteropServices;

namespace LuminaExplorer.App.Utils;

public static class UiUtils {
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB" };
    
    public static string FormatSize(long size) {
        if (size == 0)
            return "0 B";
        
        foreach (var sizeUnit in SizeUnits.SkipLast(1)) {
            if (size < 1024)
                return $"{size:#,##} {sizeUnit}";
            size /= 1024;
        }
        
        return $"{size:#,##} {SizeUnits[^1]}";
    }
    
    public static Icon? ExtractPeIcon(string filePath, int index, bool largeIcon = true) {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        nint hIcon;
        if (largeIcon)
            ExtractIconEx(filePath, index, out hIcon, IntPtr.Zero, 1);
        else
            ExtractIconEx(filePath, index, IntPtr.Zero, out hIcon, 1);

        return hIcon != IntPtr.Zero ? Icon.FromHandle(hIcon) : null;
    }

    [DllImport("shell32", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, IntPtr phiconSmall,
        int nIcons);

    [DllImport("shell32", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr phiconLarge, out IntPtr phiconSmall,
        int nIcons);
}
