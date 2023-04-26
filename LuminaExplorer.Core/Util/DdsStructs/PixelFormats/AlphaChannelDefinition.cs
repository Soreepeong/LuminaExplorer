namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public class AlphaChannelDefinition : ColorChannelDefinition {
    public readonly AlphaType AlphaType;

    public AlphaChannelDefinition() => AlphaType = AlphaType.None;

    public AlphaChannelDefinition(
        ValueType type,
        AlphaType alphaType,
        int shift,
        int bits,
        uint? mask = default) : base(type, shift, bits, mask) {
        AlphaType = bits == 0 ? AlphaType.None : alphaType;
    }

    public static AlphaChannelDefinition FromMask(ValueType valueType, AlphaType alphaType, uint mask) {
        var v = ColorChannelDefinition.FromMask(valueType, mask);
        return new(v.Type, alphaType, v.Shift, v.Bits, v.Mask);
    }
}