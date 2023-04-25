namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct LuminancePixelFormat : IPixelFormat {
    public readonly ColorChannelDefinition L;
    public readonly AlphaChannelDefinition A;
    public readonly ColorChannelDefinition X;

    public LuminancePixelFormat(
        ColorChannelDefinition l = new(),
        AlphaChannelDefinition a = new(),
        ColorChannelDefinition x = new()) {
        L = l;
        A = a;
        X = x;
    }
}