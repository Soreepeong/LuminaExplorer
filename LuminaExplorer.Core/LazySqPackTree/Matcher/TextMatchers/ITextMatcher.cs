using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public interface ITextMatcher {
    bool Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout);
    bool Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout);
    bool StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout);
    bool EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout);
}