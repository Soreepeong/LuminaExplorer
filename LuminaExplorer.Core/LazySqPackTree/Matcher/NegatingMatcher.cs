using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class NegatingMatcher : IMatcher {
    private readonly IMatcher _matcher;

    public NegatingMatcher(IMatcher matcher) {
        _matcher = matcher;
    }

    public Task<bool> Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _matcher.Matches(tree, folder, stopwatch, timeout, cancellationToken);

    public Task<bool> Matches(VirtualSqPackTree tree, VirtualFile file, Lazy<VirtualFileLookup> lookup,
        Task<Task<string>> data, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken) =>
        _matcher.Matches(tree, file, lookup, data, stopwatch, timeout, cancellationToken);

    public IMatcher UnwrapIfPossible() => _matcher is NegatingMatcher nm ? nm._matcher : this;
}
