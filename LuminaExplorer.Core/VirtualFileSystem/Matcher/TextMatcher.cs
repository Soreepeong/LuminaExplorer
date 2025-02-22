using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.VirtualFileSystem.Matcher.TextMatchers;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class TextMatcher {
    private readonly SearchEqualityType _equalityType;
    private readonly ITextMatcher _matcher;
    private readonly bool _negate;

    public TextMatcher(SearchEqualityType equalityType, SearchMatchType matchType, bool negate, string query)
    {
        this._equalityType = equalityType;
        this._matcher = matchType switch {
            SearchMatchType.Wildcard => new WildcardMatcher(query).Simplify(),
            SearchMatchType.Regex => new RegexMatcher(query),
            SearchMatchType.PlainText => new RawStringMatcher(query),
            _ => throw new InvalidOperationException(),
        };
        this._negate = negate;
    }

    public async Task<bool> Matches(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        this._negate ^ this._equalityType switch {
            SearchEqualityType.Contains => await this._matcher.Contains(
                haystack,
                stopwatch,
                timeout,
                cancellationToken),
            SearchEqualityType.Equals => await this._matcher.Equals(haystack, stopwatch, timeout, cancellationToken),
            SearchEqualityType.StartsWith => await this._matcher.StartsWith(
                haystack,
                stopwatch,
                timeout,
                cancellationToken),
            SearchEqualityType.EndsWith => await this._matcher.EndsWith(
                haystack,
                stopwatch,
                timeout,
                cancellationToken),
            _ => throw new InvalidOperationException(),
        };

    public override string ToString() =>
        this._equalityType switch {
            SearchEqualityType.Contains => $"Text({(this._negate ? "Not " : "")}...{this._matcher}...)",
            SearchEqualityType.Equals => $"Text({(this._negate ? "Not " : "")}{this._matcher})",
            SearchEqualityType.StartsWith => $"Text({(this._negate ? "Not " : "")}{this._matcher}...)",
            SearchEqualityType.EndsWith => $"Text({(this._negate ? "Not " : "")}...{this._matcher})",
            _ => $"Text({(this._negate ? "Not " : "")}{this._equalityType} {this._matcher})",
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
