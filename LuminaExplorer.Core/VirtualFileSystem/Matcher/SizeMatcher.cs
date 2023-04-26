using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class SizeMatcher {
    private readonly long _minValue, _maxValue;

    public SizeMatcher(double minValue, double maxValue) {
        _minValue = (long)minValue;
        _maxValue = (long)maxValue;
    }

    public Task<bool> Matches(long value, CancellationToken cancellationToken) =>
        Task.FromResult(_minValue <= value && value <= _maxValue);

    public override string ToString() {
        if (_minValue == 0)
            return _maxValue == long.MaxValue ? "Size(any)" : $"Size(.. {_maxValue:##,###})";
        return _maxValue == long.MaxValue ? $"Size({_minValue:##,###} ..)" : $"Size({_minValue:##,###} .. {_maxValue:##,###})";
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
