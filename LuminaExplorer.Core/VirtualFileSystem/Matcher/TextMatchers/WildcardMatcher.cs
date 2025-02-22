using System.Linq;
using System.Text.RegularExpressions;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher.TextMatchers;

public class WildcardMatcher : RegexMatcher {
    private readonly string _wildcardString;

    public WildcardMatcher(string wildcardString) : base(TransformWildcardString(wildcardString)) =>
        this._wildcardString = wildcardString;

    private static string TransformWildcardString(string s) =>
        string.Join(".*", s.Split('*').Select(y => string.Join('.', y.Split('?').Select(Regex.Escape))));

    public override string ToString() => $"Wildcard({this._regex})";

    public override ITextMatcher Simplify() =>
        this._wildcardString.Trim('*') == ""
            ? new ConstantResultTextMatcher(true)
            : this._wildcardString.Any(x => x is '*' or '?')
                ? base.Simplify()
                : new RawStringMatcher(this._wildcardString);
}
