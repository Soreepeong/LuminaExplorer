using System.Buffers;

namespace LuminaExplorer.Core.Util; 

public static class ArrayPoolExtensions {
    public static T[] RentAsNecessary<T>(this ArrayPool<T> pool, T[]? array, int minimumLength, bool clearArray = false) {
        if (array is not null && array.Length < minimumLength)
            pool.Return(ref array);

        return array ?? pool.Rent(minimumLength);
    }

    public static void Return<T>(this ArrayPool<T> pool, ref T[]? array, bool clearArray = false) {
        if (array is not null)
            pool.Return(array, clearArray);
        array = null;
    }
}