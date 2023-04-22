namespace LuminaExplorer.Core.Util;

public static class SafeDispose {
    public static void D<T>(ref T? u) where T : IDisposable {
        u?.Dispose();
        u = default;
    }
}
