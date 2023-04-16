using System.Diagnostics;
using System.Text;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher.TextMatchers;

public class RawStringMatcher : ITextMatcher {
    private readonly bool _useEscapeSequence;
    private string? _sequence;

    public RawStringMatcher(bool useEscapeSequence) {
        _useEscapeSequence = useEscapeSequence;
    }

    public string Sequence => _sequence ?? throw new InvalidOperationException();

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        var res = new List<char>();
        uint inQuote = char.MinValue;
        for (; i < span.Length; i++) {
            if (inQuote == char.MinValue && validTerminators.Contains(span[i]))
                break;

            if (!_useEscapeSequence) {
                res.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) span[i])).Select(x => (char) x));
                continue;
            }

            switch (span[i]) {
                case '"' or '\'' when inQuote == 0:
                    inQuote = span[i];
                    break;
                case '"' when inQuote == '"':
                case '\'' when inQuote == '\'':
                    inQuote = char.MinValue;
                    break;
                case '\\':
                    if (++i == span.Length)
                        return;

                    switch (span[i]) {
                        case '=':
                            ParseBase64(res, span, ref i);
                            break;
                        case '@':
                            ParseLiteral(res, span, ref i);
                            break;
                        case 'u':
                            ParseUnicode16(res, span, ref i);
                            break;
                        case 'U':
                            ParseUnicode32(res, span, ref i);
                            break;
                        case >= 0x100:
                            res.AddRange(
                                Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) span[i]))
                                    .Select(x => (char) x));
                            break;
                        default:
                            res.Add((char) (span[i] switch {
                                'a' => '\a',
                                'b' => '\b',
                                'e' => 0x1b,
                                'f' => '\f',
                                'n' => '\n',
                                'r' => '\r',
                                't' => '\t',
                                'v' => '\v',
                                >= '0' and <= '7' => ParseOctal(span, ref i),
                                'x' => ParseHex(span, ref i),
                                // 'u' => (handled above),
                                // 'U' => (handled above),
                                // >= 0x100 => (handled above),
                                _ => (char) span[i + 1],
                            }));
                            break;
                    }

                    break;
                default:
                    res.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) span[i])).Select(x => (char) x));
                    break;
            }
        }

        _sequence = new(res.ToArray());
    }

    public bool IsEmpty() => string.IsNullOrEmpty(_sequence);

    public bool Contains(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _sequence is not null
        ? haystack.Contains(_sequence)
        : throw new InvalidOperationException();

    public bool Equals(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _sequence is not null
        ? haystack == _sequence
        : throw new InvalidOperationException();

    public bool StartsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _sequence is not null
        ? haystack.StartsWith(_sequence)
        : throw new InvalidOperationException();

    public bool EndsWith(string haystack, Stopwatch stopwatch, TimeSpan timeout) => _sequence is not null
        ? haystack.EndsWith(_sequence)
        : throw new InvalidOperationException();

    private static byte ParseOctal(Span<uint> span, ref int i) {
        var n = 0u;
        for (var j = 0; j < 3 && i < span.Length; j++, i++) {
            if (span[i] == '_') {
                j--;
                continue;
            }

            if ('0' <= span[i] && span[i] <= '7')
                n = (n << 3) + span[i] - '0';
            else
                break;
        }

        // truncation may happen, but whatever.
        return unchecked((byte) n);
    }

    private static byte ParseHex(Span<uint> span, ref int i) {
        i++; // skip x
        var n = 0u;
        for (var j = 0; j < 2 && i < span.Length; j++, i++) {
            if (span[i] == '_') {
                j--;
                continue;
            }

            var m = ParseNextDigitOrMaxUint(span, i, 16);
            if (m == uint.MaxValue)
                break;
            n = (n << 4) + m;
        }

        return unchecked((byte) n);
    }

    private static void ParseUnicode16(List<char> to, Span<uint> span, ref int i) {
        i++; // skip u
        var n = 0u;
        for (var j = 0; j < 4 && i < span.Length; j++, i++) {
            if (span[i] == '_') {
                j--;
                continue;
            }

            var m = ParseNextDigitOrMaxUint(span, i, 16);
            if (m == uint.MaxValue)
                break;
            n = (n << 4) + m;
        }

        to.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) n)).Select(x => (char) x));
    }

    private static void ParseUnicode32(List<char> to, Span<uint> span, ref int i) {
        i++; // skip U
        var n = 0u;
        for (var j = 0; j < 8 && i < span.Length; j++, i++) {
            if (span[i] == '_') {
                j--;
                continue;
            }

            var m = ParseNextDigitOrMaxUint(span, i, 16);
            if (m == uint.MaxValue)
                break;
            n = (n << 8) + m;
        }

        to.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) n)).Select(x => (char) x));
    }

    public static uint ParseNextDigitOrMaxUint(Span<uint> span, int i, uint radix) {
        if (radix != 2 && radix != 8 && radix != 10 && radix != 16)
            throw new NotSupportedException();
        return span[i] switch {
            < '0' => uint.MaxValue,
            '0' or '1' => span[i] - '0',
            <= '7' when radix <= 8 => span[i] - '0',
            <= '9' when radix <= 10 => span[i] - '0',
            >= 'a' and <= 'f' => 10 + span[i] - 'a',
            >= 'A' and <= 'F' => 10 + span[i] - 'A',
            _ => uint.MaxValue
        };
    }

    private static void ParseBase64(List<char> to, Span<uint> span, ref int i) {
        i++; // skip =

        var b = new StringBuilder();
        for (; i < span.Length; i++) {
            if (span[i] == '=' && b.Length % 4 != 0)
                b.Append('=');
            else if (span[i] switch {
                         >= 'a' and <= 'z' => true,
                         >= 'A' and <= 'Z' => true,
                         >= '0' and <= '9' => true,
                         '+' or '/' => true,
                         _ => false,
                     })
                b.Append((char) span[i]);
            else if (span[i] == '-')
                b.Append('+');
            else if (span[i] == '_')
                b.Append('/');
            else
                break;
        }

        if (b.Length % 4 != 0)
            b.Append('=', 4 - b.Length % 4);

        to.AddRange(Convert.FromBase64String(b.ToString()).Select(x => (char) x));
    }

    private static void ParseLiteral(List<char> to, Span<uint> span, ref int i) {
        i++; // skip @

        // https://docs.python.org/3/library/struct.html#format-characters
        var isLittleEndian = true;
        var bitsLen = 0;
        var isInt = true;
        var isSigned = true;
        var isArray = false;
        var radix = 10u;
        var radixBits = 0;
        for (; i < span.Length; i++) {
            if (span[i] switch {
                    '<' => true,
                    '>' => false,
                    _ => (bool?) null,
                } is { } eds) {
                isLittleEndian = eds;
                continue;
            }

            if (span[i] switch {
                    'b' or 'c' => Tuple.Create(8, true, true),
                    'B' or 'C' => Tuple.Create(8, true, false),
                    'h' or 's' => Tuple.Create(16, true, true),
                    'H' or 'S' => Tuple.Create(16, true, false),
                    'i' or 'l' => Tuple.Create(32, true, true),
                    'I' or 'L' => Tuple.Create(32, true, false),
                    'q' or 'n' => Tuple.Create(64, true, true),
                    'Q' or 'N' => Tuple.Create(64, true, false),
                    'e' or 'E' => Tuple.Create(16, false, true),
                    'f' or 'F' => Tuple.Create(32, false, true),
                    'd' or 'D' => Tuple.Create(64, false, true),
                    _ => null,
                } is { } bii) {
                (bitsLen, isInt, isSigned) = bii;
                continue;
            }

            if (span[i] switch {
                    'y' or 'Y' => Tuple.Create(2u, 1),
                    'o' or 'O' => Tuple.Create(8u, 3),
                    'x' or 'X' => Tuple.Create(16u, 4),
                    _ => null,
                } is { } rdx) {
                (radix, radixBits) = rdx;
                continue;
            }

            if (span[i] == '[') {
                isArray = true;
                i++;
                break;
            }

            // This one forcefully terminates specification.
            if (span[i] == '@')
                i++;

            break;
        }

        var decimalSeparatorIsDot = Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator == ".";

        while (true) {
            var isNegative = false;
            while (i < span.Length && span[i] is '+' or '-') {
                if (span[i] == '-' && isSigned)
                    isNegative = !isNegative;
                i++;
            }

            if (isInt) {
                var un = 0ul;
                for (var j = 0; j < bitsLen && i < span.Length; i++, j += radixBits) {
                    var m = ParseNextDigitOrMaxUint(span, i, radix);
                    if (m == uint.MaxValue)
                        break;
                    un = un * radix + m;
                }

                if (isSigned && isNegative) {
                    var n = -(long) un;
                    if (isLittleEndian) {
                        for (var j = 0; j < bitsLen; j += 8)
                            to.Add((char) ((n >> j) & 0xFF));
                    } else {
                        for (var j = bitsLen - 8; j >= 0; j -= 8)
                            to.Add((char) ((n >> j) & 0xFF));
                    }
                } else {
                    if (isLittleEndian) {
                        for (var j = 0; j < bitsLen; j += 8)
                            to.Add((char) ((un >> j) & 0xFF));
                    } else {
                        for (var j = bitsLen - 8; j >= 0; j -= 8)
                            to.Add((char) ((un >> j) & 0xFF));
                    }
                }
            } else {
                var n = 0.0;
                for (var j = 0; j < bitsLen && i < span.Length; i++, j += radixBits) {
                    if (span[i] == '_') {
                        j -= radixBits;
                        continue;
                    }

                    var m = ParseNextDigitOrMaxUint(span, i, radix);
                    if (m == uint.MaxValue)
                        break;
                    n = n * radix + m;
                }

                if (i < span.Length && span[i] == (decimalSeparatorIsDot ? '.' : ',')) {
                    var rationalMultiplier = 0.1;
                    for (var j = 0; j < bitsLen && i < span.Length; i++, j += radixBits) {
                        if (span[i] == '_') {
                            j -= radixBits;
                            continue;
                        }

                        var m = ParseNextDigitOrMaxUint(span, i, radix);
                        if (m == uint.MaxValue)
                            break;
                        n += m * rationalMultiplier;
                        rationalMultiplier /= radix;
                    }
                }

                var arr = bitsLen switch {
                    16 => BitConverter.GetBytes((Half) n),
                    32 => BitConverter.GetBytes((float) n),
                    64 => BitConverter.GetBytes(n),
                    _ => throw new InvalidOperationException(), // cannot happen
                };
                if (BitConverter.IsLittleEndian != isLittleEndian)
                    Array.Reverse(arr);
                to.AddRange(arr.Select(x => (char) x).ToArray());
            }

            if (!isArray)
                break;

            while (i < span.Length) {
                var isValidSep =
                    (span[i] == ',' && decimalSeparatorIsDot) ||
                    (span[i] == '.' && !decimalSeparatorIsDot) ||
                    (span[i] < 0x10000 && char.IsWhiteSpace((char) span[i])) ||
                    span[i] switch {
                        ':' or ';' or '/' or '\\' or '^' or '|' or '!' or '~' => true,
                        _ => false,
                    };
                i++;

                if (!isValidSep) {
                    if (i < span.Length && span[i] == ']')
                        i++;
                    return;
                }
            }
        }
    }
}
