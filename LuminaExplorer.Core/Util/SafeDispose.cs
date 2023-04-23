using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.Util;

public static class SafeDispose {
    public static void One<T>(ref T? u) {
        if (u is IDisposable disposable)
            disposable.Dispose();
        u = default;
    }

    public static void Enumerable<T>(ref T? items) where T : IEnumerable {
        if (items is null)
            return;
        foreach (var item in items) {
            switch (item) {
                case IEnumerable enumerable:
                    Enumerable(ref enumerable!);
                    break;

                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        if (items is IDisposable ud)
            ud.Dispose();
    }

    public static Task OneAsync<T>(ref T? u) {
        if (u is not IAsyncDisposable asyncDisposable) {
            One(ref u);
            return Task.CompletedTask;
        }

        var t = asyncDisposable.DisposeAsync().AsTask();
        u = default;
        return t;
    }

    public static Task EnumerableAsync<T>(ref T? items) where T : IEnumerable {
        if (items is null)
            return Task.CompletedTask;

        var itemCopy = items;
        items = default;
        
        return Task.WhenAll(itemCopy.Cast<object?>()
            .Select(item => item is IEnumerable enumerable ? EnumerableAsync(ref enumerable!) : OneAsync(ref item)))
            .ContinueWith(_ => OneAsync(ref itemCopy))
            .Unwrap();
    }
}
