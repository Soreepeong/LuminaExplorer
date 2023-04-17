using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public interface ITextMatcher {
    Task<bool> Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);
    Task<bool> EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken);
}