using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class RawStringMatcher : ITextMatcher {
    private readonly string _rawString;

    public RawStringMatcher(string rawString) => _rawString = rawString;

    public override string ToString() => $"\"{_rawString}\"";

    public bool Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _rawString is not null
        ? haystack.Contains(_rawString)
        : throw new InvalidOperationException();

    public bool Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _rawString is not null
        ? haystack == _rawString
        : throw new InvalidOperationException();

    public bool StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _rawString is not null
        ? haystack.StartsWith(_rawString)
        : throw new InvalidOperationException();

    public bool EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _rawString is not null
        ? haystack.EndsWith(_rawString)
        : throw new InvalidOperationException();
}
