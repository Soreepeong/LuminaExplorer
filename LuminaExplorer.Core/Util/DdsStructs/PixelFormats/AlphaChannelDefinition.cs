namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct AlphaChannelDefinition {
    public readonly ValueType Type;
    public readonly AlphaType AlphaType;
    public readonly byte Shift;
    public readonly byte Bits;
    public readonly uint Mask;

    public AlphaChannelDefinition(
        ValueType type = ValueType.Unknown,
        AlphaType alphaType = AlphaType.None,
        int shift = 0,
        int bits = 0,
        uint mask = 0) {
        Type = bits == 0 ? ValueType.Unknown : type;
        AlphaType = bits == 0 ? AlphaType.None : alphaType;
        Shift = bits == 0 ? (byte) 0 : (byte) shift;
        Bits = (byte) bits;
        Mask = bits == 0 ? 0u : mask;
    }

    public AlphaChannelDefinition(
        ValueType type = ValueType.Unknown,
        AlphaType alphaType = AlphaType.None,
        int shift = 0,
        int bits = 0) {
        Type = bits == 0 ? ValueType.Unknown : type;
        AlphaType = bits == 0 ? AlphaType.None : alphaType;
        Shift = bits == 0 ? (byte) 0 : (byte) shift;
        Bits = (byte) bits;
        Mask = bits == 0 ? 0u : (1u << bits) - 1u;
    }

    public static AlphaChannelDefinition FromMask(ValueType valueType, AlphaType alphaType, uint mask) {
        var shift = 0;
        var bits = 0;

        while (mask != 0 && (mask & 1) == 0) {
            shift++;
            mask >>= 1;
        }

        var finalMask = mask;
        while (mask != 0) {
            if ((mask & 1) == 1)
                bits++;
            mask >>= 1;
        }

        return new(valueType, alphaType, shift, bits, finalMask);
    }
}