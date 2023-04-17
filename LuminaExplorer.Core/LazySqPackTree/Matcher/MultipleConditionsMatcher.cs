using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class MultipleConditionsMatcher : IMatcher {
    private readonly IMatcher[] _matchers;
    private readonly OperatorType _operator;
    
    public MultipleConditionsMatcher(IMatcher[] matchers, OperatorType @operator) {
        _matchers = matchers;
        _operator = @operator;
    }

    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout) =>
        _operator switch {
            OperatorType.Or => _matchers.Any(x => x.Matches(tree, folder, stopwatch, timeout)),
            OperatorType.Xor => _matchers.Any(x => x.Matches(tree, folder, stopwatch, timeout)),
            OperatorType.And or OperatorType.Default =>
                _matchers.Aggregate(false, (c, x) => c ^ x.Matches(tree, folder, stopwatch, timeout)),
            _ => throw new InvalidOperationException(),
        };

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Lazy<string> data, Stopwatch stopwatch,
        TimeSpan timeout) {
        var lookupInner = lookup;
        try {
            return _operator switch {
                OperatorType.Or => _matchers.Any(x => x.Matches(tree, file, ref lookupInner, data, stopwatch, timeout)),
                OperatorType.Xor => _matchers.Any(x =>
                    x.Matches(tree, file, ref lookupInner, data, stopwatch, timeout)),
                OperatorType.And or OperatorType.Default =>
                    _matchers.Aggregate(false, (c, x) =>
                        c ^ x.Matches(tree, file, ref lookupInner, data, stopwatch, timeout)),
                _ => throw new InvalidOperationException(),
            };
        } finally {
            lookup = lookupInner;
        }
    }

    public IMatcher UnwrapIfPossible() => _matchers.Length == 1 ? _matchers[0] : this;

    public override string ToString() => _operator switch {
        OperatorType.Or => "(" + string.Join(" || ", _matchers.Select(x => x.ToString())) + ")",
        OperatorType.Xor => "(" + string.Join(" ^ ", _matchers.Select(x => x.ToString())) + ")",
        OperatorType.And => "(" + string.Join(" && ", _matchers.Select(x => x.ToString())) + ")",
        OperatorType.Default => "(" + string.Join(" (&&) ", _matchers.Select(x => x.ToString())) + ")",
        _ => $"MultipleConditionsMatcher({_operator})",
    };

    public enum OperatorType {
        Default,
        Or,
        Xor,
        And,
    }
}
