using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class RawStringMatcher : ITextMatcher {
    private readonly string _rawString;

    public RawStringMatcher(string rawString) => _rawString = rawString;

    public override string ToString() => $"\"{_rawString}\"";

    public Task<bool> Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.Contains(_rawString));

    public Task<bool> Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack == _rawString);

    public Task<bool> StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.StartsWith(_rawString));

    public Task<bool> EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.EndsWith(_rawString));
}
