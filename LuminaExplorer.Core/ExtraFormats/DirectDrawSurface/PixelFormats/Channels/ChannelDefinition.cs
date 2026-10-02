using System;
using System.Diagnostics;

namespace LuminaExplorer.Core.ExtraFormats.DirectDrawSurface.PixelFormats.Channels;

public class ChannelDefinition : IEquatable<ChannelDefinition> {
    public static readonly ChannelDefinition Empty = new();

    public readonly ValueType Type;
    public readonly byte Shift;
    public readonly byte Bits;
    public readonly uint Mask;

    public ChannelDefinition()
    {
        this.Type = ValueType.Typeless;
        this.Mask = this.Shift = this.Bits = 0;
    }

    public ChannelDefinition(ValueType type, int shift, int bits, uint? mask = default)
    {
        mask ??= bits switch {
            32 => uint.MaxValue,
            _ => (1u << bits) - 1u,
        };
        switch (bits) {
            case < 0:
                throw new ArgumentOutOfRangeException(nameof(bits), bits, null);
            case 0:
                Debug.Assert(mask == 0);
                this.Type = ValueType.Typeless;
                this.Mask = this.Shift = this.Bits = 0;
                break;
            default:
                Debug.Assert(mask != 0);
                this.Type = type;
                this.Shift = (byte) shift;
                this.Bits = (byte) bits;
                this.Mask = mask.Value;
                break;
        }
    }

    public bool IsEmpty => this.Bits == 0;

    public float DecodeValueAsFloat(ulong data)
    {
        if (this.Bits == 0)
            return -1f;

        var v = (uint) (data >> this.Shift & this.Mask);

        switch (this.Type) {
            // Do we even have to convert?
            case ValueType.Float:
                if (this.Bits == 16)
                    return (float) BitConverter.UInt16BitsToHalf((ushort) v);
                return BitConverter.UInt32BitsToSingle(v);

            // Handle well-defined conversions first.
            case ValueType.Snorm: {
                // "-3" -3 -2 -1 0 1 2 3 => -1 -1 -2/3 -1/3 0 1/3 2/3 1
                // Handle the case where the value is or above 0.
                if (v >> (this.Bits - 1) == 0)
                    return 1f * v / (this.Mask >> 1);

                v = (~v & this.Mask) + 1;
                // Handle the case where the value is the most negative value. 
                if (v == 1u << (this.Bits - 1))
                    return -1f;
                return -1f * v / (this.Mask >> 1);
            }
            case ValueType.Unorm:
                return 1f * v / this.Mask;
            case ValueType.UnormSrgb: {
                var c = 1f * v / this.Mask;
                const float srgbToFloatThreshold = 0.04045f;
                const float srgbToFloatDenominator1 = 12.92f;
                const float srgbToFloatDenominator2 = 1.055f;
                const float srgbToFloatOffset = 0.055f;
                const float srgbToFloatExponent = 2.4f;
                if (c <= srgbToFloatThreshold)
                    return c / srgbToFloatDenominator1;
                return MathF.Pow((c + srgbToFloatOffset) / srgbToFloatDenominator2, srgbToFloatExponent);
            }

            // Handle obvious cases.
            case ValueType.Sf16:
                return (float) BitConverter.UInt16BitsToHalf((ushort) v);
            case ValueType.Uf16: {
                var exponent = (int) ((v & 0xF800) >> 11);
                var mantissa = (int) (v & 0x7FF);
                return exponent switch {
                    0 => 1f * mantissa / (1 << 25),
                    > 15 => 1f * (1f + mantissa / 2048f) / (1 << (exponent - 15)),
                    15 => 1f * (1f + mantissa / 2048f),
                    < 15 => 1f * (1f + mantissa / 2048f) * (1 << (15 - exponent)),
                };
            }

            // Approximate it with Unorm case.
            default:
                goto case ValueType.Unorm;
        }
    }

    public int DecodeValueAsUnorm(ulong data, int outBits)
    {
        if (this.Bits == 0)
            return 0;

        var v = (uint) (data >> this.Shift & this.Mask);
        switch (this.Type) {
            case ValueType.Unorm:
            case ValueType.UnormSrgb:
            case ValueType.Uint:
            case ValueType.Typeless:
                return (int) (((1 << outBits) - 1) * v / this.Mask);
            case ValueType.Snorm:
            case ValueType.Sint: {
                var negative = 0 != v >> (this.Bits - 1);
                var value = negative ? (~v & (this.Mask >> 1)) : v;
                var mid = 1 << (outBits - 1);
                value = (uint) ((mid - 1) * value / (this.Mask >> 1));
                return 0 == v >> (this.Bits - 1)
                    ? (int) (mid + value)
                    : (int) (mid - 1 - value);
            }
            default:
                return (int) Math.Clamp(
                    ((1 << outBits) - 1) * this.DecodeValueAsFloat(data),
                    0,
                    (1 << outBits) - 1);
        }
    }

    public static ChannelDefinition FromMask(ValueType valueType, uint mask)
    {
        if (mask == 0)
            return Empty;

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

    public override string ToString() => $"{(ulong) this.Mask << this.Shift:X} ({this.Type})";

    public bool Equals(ChannelDefinition? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return this.Type == other.Type && this.Shift == other.Shift && this.Bits == other.Bits &&
            this.Mask == other.Mask;
    }

    public override bool Equals(object? obj) => this.Equals(obj as ChannelDefinition);

    public override int GetHashCode() => HashCode.Combine((int) this.Type, this.Shift, this.Bits, this.Mask);
}
