using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher.TextMatchers;

public class RegexMatcher : ITextMatcher {
    protected readonly string _regex;

    public RegexMatcher(string regex)
    {
        this._regex = regex;
    }

    public override string ToString() => $"Regex({this._regex})";

    // injectable but who cares

    public Task<bool> Contains(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        =>
            this.DoMatch("{0}", haystack, stopwatch, timeout, cancellationToken);

    public Task<bool> Equals(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        =>
            this.DoMatch("^(?:{0})$", haystack, stopwatch, timeout, cancellationToken);

    public Task<bool> StartsWith(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        =>
            this.DoMatch("^(?:{0})", haystack, stopwatch, timeout, cancellationToken);

    public Task<bool> EndsWith(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        =>
            this.DoMatch("(?:{0})$", haystack, stopwatch, timeout, cancellationToken);

    private Task<bool> DoMatch(
        string matchFormat,
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (this._regex is null)
            throw new InvalidOperationException();

        var remainingTime = timeout - stopwatch.Elapsed;
        if (remainingTime < TimeSpan.Zero)
            return Task.FromResult(false);

        bool DoSearch()
        {
            try {
                return new Regex(
                    string.Format(matchFormat, this._regex),
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace |
                    RegexOptions.Singleline,
                    remainingTime).IsMatch(haystack);
            } catch (TimeoutException) {
                return false;
            }
        }

        return haystack.Length > 65536
            ? Task.Run(DoSearch, cancellationToken)
            : Task.FromResult(DoSearch());
    }

    public virtual ITextMatcher Simplify() =>
        this._regex.Replace(".*", "") == "" ? new ConstantResultTextMatcher(true) : this;
}
