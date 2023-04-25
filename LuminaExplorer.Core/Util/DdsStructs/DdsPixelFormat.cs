﻿using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.Util.DdsStructs;

[StructLayout(LayoutKind.Sequential)]
public struct DdsPixelFormat {
    public int Size;

    public DdsPixelFormatFlags Flags;

    public DdsFourCc FourCC;
    public int RgbBitCount;
    public uint RBitMask;
    public uint GBitMask;
    public uint BBitMask;
    public uint ABitMask;
}