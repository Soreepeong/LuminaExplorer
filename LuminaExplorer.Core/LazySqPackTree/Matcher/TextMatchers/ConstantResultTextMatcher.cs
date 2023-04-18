using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class ConstantResultTextMatcher : ITextMatcher {
    private readonly Task<bool> _value;

    public ConstantResultTextMatcher(bool value) => _value = Task.FromResult(value);

    public Task<bool> Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => _value;

    public Task<bool> Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => _value;

    public Task<bool> StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => _value;

    public Task<bool> EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => _value;
}
