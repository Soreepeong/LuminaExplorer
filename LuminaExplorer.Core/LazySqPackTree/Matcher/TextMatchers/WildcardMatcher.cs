using System.Text.RegularExpressions;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public partial class WildcardMatcher : RegexMatcher {
    private readonly string _wildcardString;

    public WildcardMatcher(string wildcardString) : base(TransformWildcardString(wildcardString)) =>
        _wildcardString = wildcardString;

    private static string TransformWildcardString(string s) => string.Join(
        ".*",
        DirEncompassingWildcardRegex().Split(s)
            .Select(x => string.Join(
                @"[^\\/]*",
                x.Split('*')
                    .Select(y => string.Join(
                        '.',
                        y.Split('?'))))));

    public override string ToString() => $"Wildcard({_regex})";
    
    [GeneratedRegex("\\*\\*+")]
    private static partial Regex DirEncompassingWildcardRegex();

    public ITextMatcher Simplify() => _wildcardString.Any(x => x is '*' or '?')
        ? this
        : new RawStringMatcher(_wildcardString);
}
