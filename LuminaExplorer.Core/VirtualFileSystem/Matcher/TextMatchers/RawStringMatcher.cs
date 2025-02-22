using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher.TextMatchers;

public class RawStringMatcher : ITextMatcher {
    private readonly string _rawString;

    public RawStringMatcher(string rawString) => this._rawString = rawString;

    public override string ToString() => $"\"{this._rawString}\"";

    public Task<bool> Contains(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.Contains(this._rawString));

    public Task<bool> Equals(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack == this._rawString);

    public Task<bool> StartsWith(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.StartsWith(this._rawString));

    public Task<bool> EndsWith(
        string haystack,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) => Task.FromResult(haystack.EndsWith(this._rawString));
}
