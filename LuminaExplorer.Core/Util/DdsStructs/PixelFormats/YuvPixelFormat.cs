using System;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct YuvPixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition Y;
    public readonly ColorChannelDefinition U;
    public readonly ColorChannelDefinition V;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X;

    public YuvPixelFormat(
        ColorChannelDefinition y = new(),
        ColorChannelDefinition u = new(),
        ColorChannelDefinition v = new(),
        AlphaChannelDefinition a = new(),
        ColorChannelDefinition x = new()) {
        Y = y;
        U = u;
        V = v;
        A = a;
        X = x;
    }
}