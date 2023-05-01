using System;
using System.Collections.Generic;

namespace LuminaExplorer.Core.Util; 

public class ComparisonFromDelegate<T> : IComparer<T> {
    private readonly Func<T, T, int> _cmp;
    
    public ComparisonFromDelegate(Func<T, IComparable> cmp) => _cmp = (a, b) => cmp(a).CompareTo(cmp(b));
    
    public ComparisonFromDelegate(Func<T, T, int> cmp) => _cmp = cmp;

    public int Compare(T? x, T? y) {
        if (x is null && y is null)
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        return _cmp(x, y);
    }
}
