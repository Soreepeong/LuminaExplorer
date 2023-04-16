using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class TextMatcher : IMatcher {
    private bool Negative;
    private SearchEqualityType EqualityType = SearchEqualityType.Contains;
    private ITextMatcher? Matcher;

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        var isParsingOptions = false;
        var optionsParsed = false;
        var noEscape = false;
        SearchMatchType MatchType = SearchMatchType.Wildcard;

        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;

            switch (span[i]) {
                case ':' when isParsingOptions:
                    isParsingOptions = false;
                    optionsParsed = true;
                    break;
                case '!' or '^' or '~' when isParsingOptions:
                    Negative ^= true;
                    break;
                case 'c' when isParsingOptions:
                    EqualityType = SearchEqualityType.Contains;
                    break;
                case 'e' or '=' when isParsingOptions:
                    EqualityType = SearchEqualityType.Equals;
                    break;
                case 's' or '<' when isParsingOptions:
                    EqualityType = SearchEqualityType.StartsWith;
                    break;
                case 'e' or '>' when isParsingOptions:
                    EqualityType = SearchEqualityType.EndsWith;
                    break;
                case 'w' or '*' or '?' when isParsingOptions:
                    MatchType = SearchMatchType.Wildcard;
                    break;
                case 'x' or '/' or '%' when isParsingOptions:
                    MatchType = SearchMatchType.Regex;
                    break;
                case 'p' or 't' when isParsingOptions:
                    MatchType = SearchMatchType.PlainText;
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

                    Matcher = MatchType switch {
                        SearchMatchType.Wildcard => new WildcardMatcher(!noEscape),
                        SearchMatchType.Regex => new RegexMatcher(),
                        SearchMatchType.PlainText => new RawStringMatcher(!noEscape),
                        _ => throw new InvalidOperationException()
                    };
                    break;
            }
        }
    }

    public bool IsEmpty() => Matcher?.IsEmpty() is not false;

    public bool Matches(string haystack, Stopwatch stopwatch, TimeSpan timeout) => Matcher is null
        ? throw new InvalidOperationException()
        : EqualityType switch {
            SearchEqualityType.Contains => Matcher.Contains(haystack, stopwatch, timeout),
            SearchEqualityType.Equals => Matcher.Equals(haystack, stopwatch, timeout),
            SearchEqualityType.StartsWith => Matcher.StartsWith(haystack, stopwatch, timeout),
            SearchEqualityType.EndsWith => Matcher.EndsWith(haystack, stopwatch, timeout),
            _ => throw new InvalidOperationException(),
        } ^ Negative;

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
