using System;

namespace LuminaExplorer.Core.Util;

public static class MiscUtils {
    public static float DivRem(float dividend, float divisor, out float remainder) {
        remainder = dividend % divisor;
        return (int) Math.Floor(dividend / divisor);
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
}
