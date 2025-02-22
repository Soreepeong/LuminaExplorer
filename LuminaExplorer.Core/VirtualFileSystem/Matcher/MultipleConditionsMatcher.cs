using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class MultipleConditionsMatcher : IMatcher {
    private readonly IMatcher[] _matchers;
    private readonly OperatorType _operator;

    public MultipleConditionsMatcher(IMatcher[] matchers, OperatorType @operator)
    {
        this._matchers = matchers;
        this._operator = @operator;
    }

    public async Task<bool> Matches(
        IVirtualFileSystem tree,
        IVirtualFolder folder,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(
            this._matchers.Select(
                x =>
                    x.Matches(tree, folder, stopwatch, timeout, cancellationToken)));
        return this._operator switch {
            OperatorType.Or => results.Any(x => x),
            OperatorType.Xor => results.Aggregate(false, (c, x) => c ^ x),
            OperatorType.And or OperatorType.Default => results.All(x => x),
            _ => throw new InvalidOperationException(),
        };
    }

    public async Task<bool> Matches(
        IVirtualFileSystem tree,
        IVirtualFile file,
        Lazy<IVirtualFileLookup> lookup,
        Task<Task<string>> data,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(
            this._matchers.Select(
                x =>
                    x.Matches(tree, file, lookup, data, stopwatch, timeout, cancellationToken)));
        return this._operator switch {
            OperatorType.Or => results.Any(x => x),
            OperatorType.Xor => results.Aggregate(false, (c, x) => c ^ x),
            OperatorType.And or OperatorType.Default => results.All(x => x),
            _ => throw new InvalidOperationException(),
        };
    }

    public IMatcher UnwrapIfPossible() => this._matchers.Length == 1 ? this._matchers[0] : this;

    public override string ToString() =>
        this._operator switch {
            OperatorType.Or => "(" + string.Join(" || ", this._matchers.Select(x => x.ToString())) + ")",
            OperatorType.Xor => "(" + string.Join(" ^ ", this._matchers.Select(x => x.ToString())) + ")",
            OperatorType.And => "(" + string.Join(" && ", this._matchers.Select(x => x.ToString())) + ")",
            OperatorType.Default => "(" + string.Join(" (&&) ", this._matchers.Select(x => x.ToString())) + ")",
            _ => $"MultipleConditionsMatcher({this._operator})",
        };

    public enum OperatorType {
        Default,
        Or,
        Xor,
        And,
    }
}
