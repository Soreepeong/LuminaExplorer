using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public static class SafeDispose {
    public static void One<T>(ref T? u) where T : IDisposable {
        u?.Dispose();
        u = default;
    }

    public static Task OneAsync<T>(ref T? u) where T : IAsyncDisposable {
        var t = u?.DisposeAsync().AsTask();
        u = default;
        return t ?? Task.CompletedTask;
    }

    public static void Array<T>(ref T[]? u) where T : IDisposable {
        if (u is not null)
            Enumerable(u);
        u = null;
    }

    public static void Enumerable<T>(IEnumerable<T> u) where T : IDisposable {
        foreach (var v in u)
            v?.Dispose();
    }
}
