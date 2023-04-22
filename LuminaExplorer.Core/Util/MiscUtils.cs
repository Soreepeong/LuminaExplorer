namespace LuminaExplorer.Core.Util;

public static class MiscUtils {
    public static float DivRem(float dividend, float divisor, out float remainder) {
        remainder = dividend % divisor;
        return (int) Math.Floor(dividend / divisor);
    }
}
