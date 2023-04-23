using System.Linq;
using System.Text.RegularExpressions;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class WildcardMatcher : RegexMatcher {
    private readonly string _wildcardString;

    public WildcardMatcher(string wildcardString) : base(TransformWildcardString(wildcardString)) =>
        _wildcardString = wildcardString;

    private static string TransformWildcardString(string s) =>
        string.Join(".*", s.Split('*').Select(y => string.Join('.', y.Split('?').Select(Regex.Escape))));

    public override string ToString() => $"Wildcard({_regex})";

    public override ITextMatcher Simplify() =>
        _wildcardString.Trim('*') == ""
            ? new ConstantResultTextMatcher(true)
            : _wildcardString.Any(x => x is '*' or '?')
                ? base.Simplify()
                : new RawStringMatcher(_wildcardString);
}
