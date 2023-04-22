namespace LuminaExplorer.Core.Util;

public static class SafeDispose {
    public static void One<T>(ref T? u) where T : IDisposable {
        u?.Dispose();
        u = default;
    }

    public static void Array<T>(ref T?[] u) where T : IDisposable {
        Enumerable(u);
        u = System.Array.Empty<T>();
    }

    public static void Enumerable<T>(IEnumerable<T?> u) where T : IDisposable {
        foreach (var v in u)
            v?.Dispose();
    }
}
