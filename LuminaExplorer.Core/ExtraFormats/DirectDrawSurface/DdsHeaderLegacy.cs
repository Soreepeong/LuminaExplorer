using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;

[StructLayout(LayoutKind.Sequential)]
public struct DdsHeaderLegacy {
    public const uint MagicValue = 0x20534444;

    public uint Magic;
    public DdsHeader Header;
}