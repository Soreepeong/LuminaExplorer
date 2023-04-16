using System.Text;
using System.Text.RegularExpressions;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class WildcardMatcher : RegexMatcher {
    private readonly bool _useEscapeSequence;

    public WildcardMatcher(bool useEscapeSequence) {
        _useEscapeSequence = useEscapeSequence;
    }

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        validTerminators = validTerminators.Append('*').Append('?').ToArray();

        var rsm = new RawStringMatcher(_useEscapeSequence);
        var re = new StringBuilder();

        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;
            
            switch (span[i]) {
                case '*':
                    var wildcardCount = 1;
                    while (i + 1 < span.Length && span[i + 1] == '*') {
                        i++;
                        wildcardCount++;
                    }

                    re.Append(wildcardCount == 1 ? @"[^\\/]*" : @".*");
                    break;
                case '?':
                    re.Append('.');
                    break;
                default:
                    rsm.ParseQuery(span, ref i, validTerminators);
                    re.Append(Regex.Escape(rsm.Sequence));
                    break;
            }
        }

        _regex = re.ToString();
    }
}
