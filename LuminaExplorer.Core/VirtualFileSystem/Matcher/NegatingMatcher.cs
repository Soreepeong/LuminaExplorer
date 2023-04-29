using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class NegatingMatcher : IMatcher {
    private readonly IMatcher _matcher;

    public NegatingMatcher(IMatcher matcher) {
        _matcher = matcher;
    }

    public Task<bool> Matches(IVirtualFileSystem tree, IVirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _matcher.Matches(tree, folder, stopwatch, timeout, cancellationToken)
            .ContinueWith(x => !x.Result, cancellationToken);

    public Task<bool> Matches(IVirtualFileSystem tree, IVirtualFile file, Lazy<IVirtualFileLookup> lookup,
        Task<Task<string>> data, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken) =>
        _matcher.Matches(tree, file, lookup, data, stopwatch, timeout, cancellationToken)
            .ContinueWith(x => !x.Result, cancellationToken);

    public IMatcher UnwrapIfPossible() => _matcher is NegatingMatcher nm ? nm._matcher : this;
}
