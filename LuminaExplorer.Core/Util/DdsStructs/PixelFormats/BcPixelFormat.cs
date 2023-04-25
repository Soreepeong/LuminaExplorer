namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct BcPixelFormat : IPixelFormat {
    public readonly ValueType Type;
    public readonly AlphaType Alpha;
    public readonly byte Version;

    public BcPixelFormat(
        ValueType type = ValueType.Unknown,
        AlphaType alpha = AlphaType.Straight,
        byte version = 0) {
        Type = type;
        Alpha = alpha;
        Version = version;
    }
}