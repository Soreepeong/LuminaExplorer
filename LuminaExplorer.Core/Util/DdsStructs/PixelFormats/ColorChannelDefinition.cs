namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public readonly struct ColorChannelDefinition {
    public readonly ValueType Type;
    public readonly byte Shift;
    public readonly byte Bits;
    public readonly uint Mask;

    public ColorChannelDefinition(ValueType type = ValueType.Unknown, int shift = 0, int bits = 0) {
        Type = bits == 0 ? ValueType.Unknown : type;
        Shift = bits == 0 ? (byte) 0 : (byte) shift;
        Bits = (byte) bits;
        Mask = bits == 0 ? 0u : (1u << bits) - 1u;
    }

    public ColorChannelDefinition(ValueType type = ValueType.Unknown, int shift = 0, int bits = 0, uint mask = 0) {
        Type = bits == 0 ? ValueType.Unknown : type;
        Shift = bits == 0 ? (byte) 0 : (byte) shift;
        Bits = (byte) bits;
        Mask = bits == 0 ? 0u : mask;
    }

    public static ColorChannelDefinition FromMask(ValueType valueType, uint mask) {
        if (mask == 0)
            return new();

        var shift = 0;
        var bits = 0;

        while (mask != 0 && (mask & 1) == 0) {
            shift++;
            mask >>= 1;
        }

        while (mask != 0) {
            bits++;
            mask >>= 1;
        }

        return new(valueType, shift, bits);
    }
}