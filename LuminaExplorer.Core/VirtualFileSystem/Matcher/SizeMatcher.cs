using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class SizeMatcher {
    private readonly long _minValue, _maxValue;

    public SizeMatcher(double minValue, double maxValue)
    {
        this._minValue = (long) minValue;
        this._maxValue = (long) maxValue;
    }

    public Task<bool> Matches(long value, CancellationToken cancellationToken) =>
        Task.FromResult(this._minValue <= value && value <= this._maxValue);

    public override string ToString()
    {
        if (this._minValue == 0)
            return this._maxValue == long.MaxValue ? "Size(any)" : $"Size(.. {this._maxValue:##,###})";
        return this._maxValue == long.MaxValue
            ? $"Size({this._minValue:##,###} ..)"
            : $"Size({this._minValue:##,###} .. {this._maxValue:##,###})";
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
