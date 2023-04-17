using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class NegatingMatcher : IMatcher {
    private readonly IMatcher _matcher;

    public NegatingMatcher(IMatcher matcher) {
        _matcher = matcher;
    }

    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout) =>
        _matcher.Matches(tree, folder, stopwatch, timeout);

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Lazy<string> data,
        Stopwatch stopwatch, TimeSpan timeout) => _matcher.Matches(tree, file, ref lookup, data, stopwatch, timeout);

    public IMatcher UnwrapIfPossible() => _matcher is NegatingMatcher nm ? nm._matcher : this;
}
