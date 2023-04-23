using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class TextMatcher {
    private readonly SearchEqualityType _equalityType;
    private readonly ITextMatcher _matcher;
    private readonly bool _negate;

    public TextMatcher(SearchEqualityType equalityType, SearchMatchType matchType, bool negate, string query) {
        _equalityType = equalityType;
        _matcher = matchType switch {
            SearchMatchType.Wildcard => new WildcardMatcher(query).Simplify(),
            SearchMatchType.Regex => new RegexMatcher(query),
            SearchMatchType.PlainText => new RawStringMatcher(query),
            _ => throw new InvalidOperationException(),
        };
        _negate = negate;
    }
    
    public async Task<bool> Matches(string haystack, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) => _negate ^ _equalityType switch {
            SearchEqualityType.Contains => await _matcher.Contains(haystack, stopwatch, timeout, cancellationToken),
            SearchEqualityType.Equals => await _matcher.Equals(haystack, stopwatch, timeout, cancellationToken),
            SearchEqualityType.StartsWith => await _matcher.StartsWith(haystack, stopwatch, timeout, cancellationToken),
            SearchEqualityType.EndsWith => await _matcher.EndsWith(haystack, stopwatch, timeout, cancellationToken),
            _ => throw new InvalidOperationException(),
        };

    public override string ToString() => _equalityType switch {
        SearchEqualityType.Contains => $"Text({(_negate ? "Not " : "")}...{_matcher}...)",
        SearchEqualityType.Equals => $"Text({(_negate ? "Not " : "")}{_matcher})",
        SearchEqualityType.StartsWith => $"Text({(_negate ? "Not " : "")}{_matcher}...)",
        SearchEqualityType.EndsWith => $"Text({(_negate ? "Not " : "")}...{_matcher})",
        _ => $"Text({(_negate ? "Not " : "")}{_equalityType} {_matcher})",
    };

    public enum SearchEqualityType {
        Contains,
        Equals,
        StartsWith,
        EndsWith,
        Invalid = int.MaxValue,
    }

    public enum SearchMatchType {
        Wildcard,
        Regex,
        PlainText,
        Invalid = int.MaxValue,
    }
}
