namespace LuminaExplorer.Core.Util;

public static class AsyncSorter {
    public static AsyncSorter<T> SortAsync<T>(this ICollection<T> collection) => new(collection);
}
