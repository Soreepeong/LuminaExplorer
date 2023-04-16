using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class TextMatcher : IMatcher {
    private bool _negative;
    private SearchEqualityType _equalityType = SearchEqualityType.Contains;
    private ITextMatcher? _matcher;

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        var isParsingOptions = false;
        var optionsParsed = false;
        var noEscape = false;
        var matchType = SearchMatchType.Wildcard;

        _negative = false;
        _equalityType = SearchEqualityType.Contains;
        _matcher = null;

        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;

            if (!IsEmpty())
                break;

            switch (span[i]) {
                case ':' when isParsingOptions:
                    isParsingOptions = false;
                    optionsParsed = true;
                    break;
                case '!' or '^' or '~' when isParsingOptions:
                    _negative ^= true;
                    break;
                case 'c' when isParsingOptions:
                    _equalityType = SearchEqualityType.Contains;
                    break;
                case 'e' or '=' when isParsingOptions:
                    _equalityType = SearchEqualityType.Equals;
                    break;
                case 's' or '<' when isParsingOptions:
                    _equalityType = SearchEqualityType.StartsWith;
                    break;
                case 'e' or '>' when isParsingOptions:
                    _equalityType = SearchEqualityType.EndsWith;
                    break;
                case 'w' or '*' or '?' when isParsingOptions:
                    matchType = SearchMatchType.Wildcard;
                    break;
                case 'x' or '/' or '%' when isParsingOptions:
                    matchType = SearchMatchType.Regex;
                    break;
                case 'p' or 't' when isParsingOptions:
                    matchType = SearchMatchType.PlainText;
                    break;
                case 'r' or '@' when isParsingOptions:
                    noEscape = true;
                    break;
                case ':' when !isParsingOptions && !optionsParsed:
                    isParsingOptions = true;
                    break;
                default:
                    if (isParsingOptions)
                        isParsingOptions = false;

                    optionsParsed = true;

                    _matcher = matchType switch {
                        SearchMatchType.Wildcard => new WildcardMatcher(!noEscape),
                        SearchMatchType.Regex => new RegexMatcher(),
                        SearchMatchType.PlainText => new RawStringMatcher(!noEscape),
                        _ => throw new InvalidOperationException()
                    };
                    break;
            }
        }
    }

    public bool IsEmpty() => _matcher?.IsEmpty() is not false;

    public bool Matches(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _matcher is null
        ? throw new InvalidOperationException()
        : _equalityType switch {
            SearchEqualityType.Contains => _matcher.Contains(haystack, stopwatch, timeout),
            SearchEqualityType.Equals => _matcher.Equals(haystack, stopwatch, timeout),
            SearchEqualityType.StartsWith => _matcher.StartsWith(haystack, stopwatch, timeout),
            SearchEqualityType.EndsWith => _matcher.EndsWith(haystack, stopwatch, timeout),
            _ => throw new InvalidOperationException(),
        } ^ _negative;

    public enum SearchEqualityType {
        Contains,
        Equals,
        StartsWith,
        EndsWith,
    }

    public enum SearchMatchType {
        Wildcard,
        Regex,
        PlainText,
    }
}
