using System;
using System.Text;

namespace LuminaExplorer.Core.Util;

public static class MiscUtils {
    public static int PositiveMod(int dividend, int divisor) {
        var r = dividend % divisor;
        return r >= 0 ? r : divisor + r;
    }

    public static float PositiveMod(float dividend, float divisor) {
        var r = dividend % divisor;
        return r >= 0 ? r : divisor + r;
    }

    public static float DivRem(float dividend, float divisor, out float remainder) {
        remainder = dividend % divisor;
        return (int) Math.Floor(dividend / divisor);
    }

    public static unsafe string GetStringNullTerminated(this Encoding encoding, ReadOnlySpan<byte> data) {
        // can IsSingleByte + encoding.GetMaxByteCount() be used to determine this value?
        var len = 0;
        switch (encoding.CodePage) {
            case 1200 or 1201:
                fixed (byte* pData = &data.GetPinnableReference()) {
                    var span2 = new ReadOnlySpan<ushort>(pData, data.Length / 2);
                    while (len < span2.Length && span2[len] != 0)
                        len++;
                    return encoding.GetString(data[..(len * 2)]);
                }
            case 12000 or 12001:
                fixed (byte* pData = &data.GetPinnableReference()) {
                    var span2 = new ReadOnlySpan<uint>(pData, data.Length / 4);
                    while (len < span2.Length && span2[len] != 0)
                        len++;
                    return encoding.GetString(data[..(len * 4)]);
                }
            default:
                while (len < data.Length && data[len] != 0)
                    len++;
                return encoding.GetString(data[..len]);
        }
    }

    public static int CompareNullable<T>(T? v1, T? v2) where T : class, IComparable<T> {
        if (v1 is null && v2 is null)
            return 0;
        if (v1 is null)
            return -1;
        if (v2 is null)
            return 1;
        return v1.CompareTo(v2);
    }

    public static int CompareNullable<T>(T? v1, T? v2) where T : struct, IComparable<T> {
        if (v1 is null && v2 is null)
            return 0;
        if (v1 is null)
            return -1;
        if (v2 is null)
            return 1;
        return v1.Value.CompareTo(v2.Value);
    }

    public static int CompareNatural(string str1, string str2) {
        var span1 = str1.AsSpan();
        var span2 = str2.AsSpan();

        // Step 0. Case-insensitive, integer-aware natural sort
        // Step 1. Case-insensitive sort
        // Step 2. Case-sensitive sort
        for (var step = 0; step < 3; step++) {
            var p1 = 0;
            var p2 = 0;

            while (p1 < span1.Length && p2 < span2.Length) {
                var c1 = span1[p1];
                var c2 = span2[p1];
                var i1 = p1;
                var i2 = p2;

                if (step is 0 or 1) {
                    c1 = char.ToLowerInvariant(c1);
                    c2 = char.ToLowerInvariant(c2);
                }

                if (step is 0) {
                    if (char.IsNumber(c1) && char.IsNumber(c2)) {
                        for (p1++; p1 < span1.Length; p1++)
                            if (!char.IsNumber(span1[p1]))
                                break;

                        for (p2++; p2 < span2.Length; p2++)
                            if (!char.IsNumber(span2[p2]))
                                break;

                        var n1 = double.Parse(span1[i1..p1]);
                        var n2 = double.Parse(span2[i2..p2]);
                        if (n1 < n2)
                            return -1;
                        if (n1 > n2)
                            return 1;

                        continue;
                    }
                }

                if (c1 < c2)
                    return -1;
                if (c1 > c2)
                    return 1;

                p1++;
                p2++;
            }

            if (p1 < p2)
                return -1;
            if (p1 > p2)
                return 1;
        }

        return 0;
    }
}
