using System;

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

    public int DecodeValueAsByte(ulong data, int outBits) {
        var v = (uint)(data >> Shift & Mask);
        switch (Type) {
            case ValueType.Sint:
            case ValueType.Snorm: {
                var negative = 0 != v >> (Bits - 1);
                var value = negative ? (~v & (Mask >> 1)) : v;
                var mid = 1 << (outBits - 1);
                value = (uint) ((mid - 1) * value / (Mask >> 1));
                return 0 == v >> (Bits - 1) 
                    ? (int) (mid + value)
                    : (int) (mid - 1 - value);
            }
            case ValueType.Uint:
            case ValueType.Unorm:
            case ValueType.Typeless:
            case ValueType.Unknown:
                return (int)(((1 << outBits) - 1) * v / Mask);
            case ValueType.UnormSrgb:
                // TODO: srgb conversion
                return (int)(((1 << outBits) - 1) * v / Mask);
            case ValueType.Sf16:
            case ValueType.Uf16:
                // Irrelevant with this format, but just in case
                goto case ValueType.Unorm;
            case ValueType.Float:
                unsafe {
                    return (int) Math.Round(((1 << outBits) - 1) * BitConverter.ToSingle(new(&v, 4)));
                }
            default:
                goto case ValueType.Unorm;
        }
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