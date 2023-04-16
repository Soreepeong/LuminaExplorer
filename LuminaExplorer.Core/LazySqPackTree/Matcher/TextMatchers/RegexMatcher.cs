using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class RegexMatcher : ITextMatcher {
    protected string? _regex;

    public virtual void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        validTerminators = validTerminators.Append('*').Append('?').ToArray();
        var rsm = new RawStringMatcher(false);
        rsm.ParseQuery(span, ref i, validTerminators);
        _regex = rsm.Sequence;
    }

    public bool IsEmpty() => string.IsNullOrEmpty(_regex);

    public override string ToString() => $"Regex({_regex})";

    // injectable but who cares
    
    public bool Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout)
        => DoMatch("{0}", haystack, stopwatch, timeout);

    public bool Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout)
        => DoMatch("^(?:{0})$", haystack, stopwatch, timeout);

    public bool StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout)
        => DoMatch("^(?:{0})", haystack, stopwatch, timeout);

    public bool EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout)
        => DoMatch("(?:{0})$", haystack, stopwatch, timeout);
    
    private bool DoMatch(string matchFormat, string haystack, Stopwatch stopwatch, TimeSpan timeout) {
        if (_regex is null)
            throw new InvalidOperationException();
        var remainingTime = timeout - stopwatch.Elapsed;
        if (remainingTime < TimeSpan.Zero)
            throw new TimeoutException();
        return new Regex(string.Format(matchFormat, _regex),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
            RegexOptions.Singleline, remainingTime).IsMatch(haystack);
    }
}
