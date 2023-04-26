using System;
using System.Diagnostics;

namespace LuminaExplorer.Core.Util.DdsStructs.PixelFormats;

public class ColorChannelDefinition {
    public readonly ValueType Type;
    public readonly byte Shift;
    public readonly byte Bits;
    public readonly uint Mask;

    public ColorChannelDefinition() {
        Type = ValueType.Unknown;
        Mask = Shift = Bits = 0;
    }

    public ColorChannelDefinition(ValueType type, int shift, int bits, uint? mask = default) {
        mask ??= bits switch {
            32 => uint.MaxValue,
            _ => (1u << bits) - 1u,
        };
        switch (bits) {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(bits), bits, null);
            case 0:
                Debug.Assert(mask == 0);
                Type = ValueType.Unknown;
                Mask = Shift = Bits = 0;
                break;
            default:
                Debug.Assert(mask != 0);
                Type = type;
                Shift = (byte) shift;
                Bits = (byte) bits;
                Mask = mask.Value;
                break;
        }
    }

    public float DecodeValueAsFloat(ulong data) {
        if (Bits == 0)
            return -1f;
        
        var v = (uint) (data >> Shift & Mask);
        switch (Type) {
            case ValueType.Snorm:
            case ValueType.Sint: {
                if (v >> (Bits - 1) == 0)
                    return 1f * v / (Mask >> 1);
                v = (~v & Mask) + 1;
                if (v == 1 << Bits)
                    return -1f;
                return -1f * v / (Mask >> 1);
            }
            case ValueType.Unorm:
            case ValueType.UnormSrgb:
            case ValueType.Uint:
            case ValueType.Typeless:
            case ValueType.Unknown:
                return 1f * v / Mask;
            case ValueType.Sf16:
            case ValueType.Uf16:
                // Irrelevant with this format, but just in case
                goto case ValueType.Unorm;
            case ValueType.Float:
                unsafe {
                    var f = 0f;
                    *(uint*) &f = v;
                    return f;
                }
            default:
                // "Approximate" it
                goto case ValueType.Unorm;
        }
    }

    public int DecodeValueAsInt(ulong data, int outBits) {
        if (Bits == 0)
            return 0;
        
        var v = (uint) (data >> Shift & Mask);
        switch (Type) {
            case ValueType.Snorm:
            case ValueType.Sint: {
                var negative = 0 != v >> (Bits - 1);
                var value = negative ? (~v & (Mask >> 1)) : v;
                var mid = 1 << (outBits - 1);
                value = (uint) ((mid - 1) * value / (Mask >> 1));
                return 0 == v >> (Bits - 1)
                    ? (int) (mid + value)
                    : (int) (mid - 1 - value);
            }
            case ValueType.Unorm:
            case ValueType.UnormSrgb:
            case ValueType.Uint:
            case ValueType.Typeless:
            case ValueType.Unknown:
                return (int) (((1 << outBits) - 1) * v / Mask);
            case ValueType.Sf16:
            case ValueType.Uf16:
                // Irrelevant with this format, but just in case
                goto case ValueType.Unorm;
            case ValueType.Float:
                unsafe {
                    return (int) Math.Round(((1 << outBits) - 1) * BitConverter.ToSingle(new(&v, 4)));
                }
            default:
                // "Approximate" it
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
