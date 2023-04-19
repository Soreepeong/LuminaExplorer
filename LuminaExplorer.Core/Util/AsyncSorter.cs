namespace LuminaExplorer.Core.Util;

public static class AsyncListSorter {
    public static AsyncListSorter<T> SortIntoNewListAsync<T>(this ICollection<T> collection) => new(new(collection));
    public static AsyncListSorter<T> SortAsync<T>(this List<T> list) => new(list);
}
