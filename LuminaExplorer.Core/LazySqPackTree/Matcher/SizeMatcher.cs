using LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class SizeMatcher : IMatcher {
    private ComparisonType _comparisonType = ComparisonType.Equals;
    private ulong? _value;

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        _value = null;
        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;

            if (_value is not null)
                continue;

            switch (span[i]) {
                case '=':
                    _comparisonType = ComparisonType.Equals;
                    i++;
                    break;
                case '<':
                    _comparisonType = ComparisonType.AtMost;
                    i++;
                    if (i < span.Length && span[i] == '=')
                        i++;
                    break;
                case '>':
                    _comparisonType = ComparisonType.AtLeast;
                    i++;
                    if (i < span.Length && span[i] == '=')
                        i++;
                    break;
            }

            var radix = 10u;
            if (i + 1 < span.Length && span[i] == '0') {
                i++;
                switch (span[i]) {
                    case 'b' or 'B':
                        radix = 2;
                        i++;
                        break;
                    case 'o' or 'O':
                        radix = 8;
                        i++;
                        break;
                    case 'x' or 'X':
                        radix = 16;
                        i++;
                        break;
                }
            }

            var n = 0ul;
            var foundAny = false;
            for (; i < span.Length; i++) {
                if (span[i] == '_')
                    continue;

                var m = RawStringMatcher.ParseNextDigitOrMaxUint(span, i, radix);
                if (m == uint.MaxValue)
                    break;
                n = n * radix + m;
                foundAny = true;
            }

            if (!foundAny)
                continue;

            while (i + 1 < span.Length) {
                switch (span[i + 1]) {
                    case 'b' or 'B':
                        i++;
                        break;
                    case 'k' or 'K':
                        n *= 1024ul;
                        i++;
                        break;
                    case 'm' or 'M':
                        n *= 1024ul * 1024;
                        i++;
                        break;
                    case 'g' or 'G':
                        n *= 1024ul * 1024 * 1024;
                        i++;
                        break;
                    case 't' or 'T':
                        n *= 1024ul * 1024 * 1024 * 1024;
                        i++;
                        break;
                }
            }
            
            _value = n;
        }
    }

    public bool IsEmpty() => _value is null;

    public bool Matches(ulong value) => _value is null
        ? throw new InvalidOperationException()
        : _comparisonType switch {
            ComparisonType.Equals => value == _value.Value,
            ComparisonType.AtLeast => value >= _value.Value,
            ComparisonType.AtMost => value <= _value.Value,
            _ => throw new InvalidOperationException(),
        };

    public enum ComparisonType {
        Equals,
        AtLeast,
        AtMost,
    }
}
