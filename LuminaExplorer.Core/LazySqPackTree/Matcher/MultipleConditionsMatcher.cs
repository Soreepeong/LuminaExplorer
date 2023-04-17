using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class MultipleConditionsMatcher : IMatcher {
    private readonly IMatcher[] _matchers;
    private readonly OperatorType _operator;
    
    public MultipleConditionsMatcher(IMatcher[] matchers, OperatorType @operator) {
        _matchers = matchers;
        _operator = @operator;
    }

    public async Task<bool> Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) {
        var results = await Task.WhenAll(_matchers.Select(x =>
            x.Matches(tree, folder, stopwatch, timeout, cancellationToken)));
        return _operator switch {
            OperatorType.Or => results.Any(x => x),
            OperatorType.Xor => results.Aggregate(false, (c, x) => c ^ x),
            OperatorType.And or OperatorType.Default => results.All(x => x),
            _ => throw new InvalidOperationException(),
        };
    }

    public async Task<bool> Matches(VirtualSqPackTree tree, VirtualFile file, Lazy<VirtualFileLookup> lookup,
        Task<Task<string>> data, Stopwatch stopwatch,
        TimeSpan timeout, CancellationToken cancellationToken) {
        var results = await Task.WhenAll(_matchers.Select(x =>
            x.Matches(tree, file, lookup, data, stopwatch, timeout, cancellationToken)));
        return _operator switch {
            OperatorType.Or => results.Any(x => x),
            OperatorType.Xor => results.Aggregate(false, (c, x) => c ^ x),
            OperatorType.And or OperatorType.Default => results.All(x => x),
            _ => throw new InvalidOperationException(),
        };
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
