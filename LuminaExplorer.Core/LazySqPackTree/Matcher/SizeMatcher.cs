namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class SizeMatcher {
    private readonly ulong _minValue, _maxValue;

    public SizeMatcher(double minValue, double maxValue) {
        _minValue = (ulong)minValue;
        _maxValue = (ulong)maxValue;
    }

    public bool Matches(ulong value) => _minValue <= value && value <= _maxValue;

    public override string ToString() {
        if (_minValue == 0)
            return _maxValue == ulong.MaxValue ? "Size(any)" : $"Size(.. {_maxValue:##,###})";
        return _maxValue == ulong.MaxValue ? $"Size({_minValue:##,###} ..)" : $"Size({_minValue:##,###} .. {_maxValue:##,###})";
    }

    public enum ComparisonType {
        Equals,
        GreaterThan,
        GreaterThanOrEquals,
        LessThan,
        LessThanOrEquals,
        Invalid = int.MaxValue,
    }
}
