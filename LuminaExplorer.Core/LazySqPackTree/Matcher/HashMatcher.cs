using LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class HashMatcher : IMatcherComponent {
    private uint[]? _values;

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        _values = null;
        
        var values = new List<uint>();
        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;

            var n = 0u;
            var foundAny = false;
            for (var j = 0; j < 8 && i < span.Length; i++, j++) {
                if (span[i] == '_') {
                    j--;
                    continue;
                }

                var m = RawStringMatcher.ParseNextDigitOrMaxUint(span, i, 16);
                if (m == uint.MaxValue)
                    break;
                n = (n << 8) + m;
                foundAny = true;
            }

            if (foundAny) {
                values.Add(n);
                i--;
            }
        }

        _values = values.ToArray();
    }

    public bool IsEmpty() => _values?.Any() is not true;

    public bool Matches(uint hash) => _values?.Contains(hash) ?? throw new InvalidOperationException();

    public override string ToString() => $"Hash({string.Join(", ", _values ?? Array.Empty<uint>())})";
}
