namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public enum SIGDN : uint {
    NORMALDISPLAY = 0,
    PARENTRELATIVEPARSING = 0x80018001,
    PARENTRELATIVEFORADDRESSBAR = 0x8001c001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    PARENTRELATIVEEDITING = 0x80031001,
    DESKTOPABSOLUTEEDITING = 0x8004c000,
    FILESYSPATH = 0x80058000,
    URL = 0x80068000,
    /// <summary>
    /// Returns the path relative to the parent folder.
    /// </summary>
    PARENTRELATIVE = 0x80080001,
    /// <summary>
    /// Introduced in Windows 8.
    /// </summary>
    PARENTRELATIVEFORUI = 0x80094001

}