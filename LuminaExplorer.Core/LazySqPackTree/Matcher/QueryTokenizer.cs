using System.Text;
using LuminaExplorer.Core.Util;
using Microsoft.Extensions.ObjectPool;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class QueryTokenizer {
    private readonly string _query;

    private readonly ObjectPool<StringBuilder> _stringBuilderPool =
        ObjectPool.Create(new StringBuilderPooledObjectPolicy());

    private readonly ObjectPool<MemoryStream> _memoryStreamPool =
        ObjectPool.Create(new MemoryStreamPooledObjectPolicy());

    public QueryTokenizer(string query) {
        _query = query;
    }

    public IMatcher? Parse() => _NextQuery(0, out _, out var o) ? o : null;

    private bool _NextQuery(int i, out int next, out IMatcher o, params uint[] extraTerminatorsIfUnescaped) => 
        _NextQueryOperator(i, out next, out o, MultipleConditionsMatcher.OperatorType.Default, extraTerminatorsIfUnescaped);

    private bool _NextQueryOperator(int i, out int next, out IMatcher o,
        MultipleConditionsMatcher.OperatorType @operator, params uint[] extraTerminatorsIfUnescaped) {

        o = null!;
        var matchers = new List<IMatcher>();
        for (; ; i = next) {
            if (matchers.Any()) {
                if (!_NextWhitespace(i, out next))
                    break;

                if (@operator != MultipleConditionsMatcher.OperatorType.Default) {
                    var opName = @operator switch {
                        MultipleConditionsMatcher.OperatorType.Or => "OR",
                        MultipleConditionsMatcher.OperatorType.Xor => "XOR",
                        MultipleConditionsMatcher.OperatorType.And => "AND",
                        _ => throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null),
                    };
                    if (!_NextExactLiteral(next, out next, opName, opName.Length, false, false))
                        break;

                    if (!_NextWhitespace(next, out next))
                        break;
                }

                i = next;
            }

            IMatcher? matcher;
            switch (@operator) {
                case MultipleConditionsMatcher.OperatorType.Default:
                    if (!_NextQueryOperator(i, out next, out matcher, MultipleConditionsMatcher.OperatorType.Or,
                            extraTerminatorsIfUnescaped))
                        matcher = null;
                    break;
                case MultipleConditionsMatcher.OperatorType.Or:
                    if (!_NextQueryOperator(i, out next, out matcher, MultipleConditionsMatcher.OperatorType.Xor,
                            extraTerminatorsIfUnescaped))
                        matcher = null;
                    break;
                case MultipleConditionsMatcher.OperatorType.Xor:
                    if (!_NextQueryOperator(i, out next, out matcher, MultipleConditionsMatcher.OperatorType.And,
                            extraTerminatorsIfUnescaped))
                        matcher = null;
                    break;
                case MultipleConditionsMatcher.OperatorType.And:
                    if (!_NextCondition(i, out next, out matcher, extraTerminatorsIfUnescaped))
                        matcher = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
            }

            if (matcher is null) {
                if (@operator != MultipleConditionsMatcher.OperatorType.Default)
                    break;
            } else
                matchers.Add(matcher);
        }

        if (!matchers.Any())
            return false;

        next = i;
        o = new MultipleConditionsMatcher(matchers.ToArray(), @operator).UnwrapIfPossible();
        return true;
    }

    private bool _NextCondition(int i, out int next, out IMatcher o, params uint[] extraTerminatorsIfUnescaped) {
        o = null!;

        if (_NextValidCodepoint(i, out next, out var c)) {
            if (c is '(' or '[' or '{' or '<') {
                i = next;

                var terminator = c switch {
                    '(' => ')',
                    '[' => ']',
                    '{' => '}',
                    '<' => '>',
                    _ => throw new FailFastException("([{<"),
                };
                if (!_NextQuery(i, out next, out o, extraTerminatorsIfUnescaped.Append(terminator).ToArray()))
                    return false;

                i = next;
                DrainWhitespaces(ref i);

                return _NextValidCodepoint(i, out next, out c) && c == ')';
            }
        } else {
            // All the following conditions will fail if no more characters are available.
            return false;
        }

        if (_NextExactLiteral(i, out next, "NOT", 3, false, false)) {
            if (_NextWhitespace(next, out next) && _NextCondition(next, out next, out o, extraTerminatorsIfUnescaped)) {
                o = new NegatingMatcher(o).UnwrapIfPossible();
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "name", 1, true, true, ':', '=', ':')) {
            if (_NextTextMatcher(next, out next, out var textMatcher, extraTerminatorsIfUnescaped)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.Name, textMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "path", 1, true, true, ':', '=', ':')) {
            if (_NextTextMatcher(next, out next, out var textMatcher, extraTerminatorsIfUnescaped)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.Path, textMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "data", 1, true, true, ':', '=', ':')) {
            if (_NextTextMatcher(next, out next, out var textMatcher, extraTerminatorsIfUnescaped)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.Data, textMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "type", 1, true, true, ':', '=', ':')) {
            var i2 = next;
            if (_NextExactLiteral(i2, out next, "file", 2, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.File);
                return true;
            }

            if (_NextExactLiteral(i2, out next, "folder", 2, true, false) ||
                _NextExactLiteral(i2, out next, "directory", 1, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.Folder);
                return true;
            }

            if (_NextExactLiteral(i2, out next, "empty", 1, true, false) ||
                _NextExactLiteral(i2, out next, "placeholder", 2, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.Empty);
                return true;
            }

            if (_NextExactLiteral(i2, out next, "standard", 1, true, false) ||
                _NextExactLiteral(i2, out next, "binary", 1, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.Standard);
                return true;
            }

            if (_NextExactLiteral(i2, out next, "texture", 1, true, false) ||
                _NextExactLiteral(i2, out next, "image", 1, true, false) ||
                _NextExactLiteral(i2, out next, "picture", 2, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.Texture);
                return true;
            }

            if (_NextExactLiteral(i2, out next, "model", 1, true, false) ||
                _NextExactLiteral(i2, out next, "object", 1, true, false)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.TypeConstraint.Model);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "hash", 1, true, true, ':', '=', ':')) {
            if (_NextHashMatcher(next, out next, out var hashMatcher)) {
                o = new SingleConditionMatcher(hashMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "size", 1, true, true, ':', '=', ':') ||
            _NextExactLiteral(i, out next, "rawsize", 1, true, true, ':', '=', ':') ||
            _NextExactLiteral(i, out next, "length", 1, true, true, ':', '=', ':')) {
            if (_NextSizeMatcher(next, out next, out var sizeMatcher)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.RawSize, sizeMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "occupied", 1, true, true, ':', '=', ':')) {
            if (_NextSizeMatcher(next, out next, out var sizeMatcher)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.OccupiedSize, sizeMatcher);
                return true;
            }
        }

        if (_NextExactLiteral(i, out next, "reserved", 1, true, true, ':', '=', ':')) {
            if (_NextSizeMatcher(next, out next, out var sizeMatcher)) {
                o = new SingleConditionMatcher(SingleConditionMatcher.MatchWhat.ReservedSize, sizeMatcher);
                return true;
            }
        }

        if (_NextTextFallbackMatcher(i, out next, out var defaultTextMatcher, out var isPath, extraTerminatorsIfUnescaped)) {
            o = new SingleConditionMatcher(
                isPath ? SingleConditionMatcher.MatchWhat.Path : SingleConditionMatcher.MatchWhat.Name,
                defaultTextMatcher);
            return true;
        }

        return false;
    }

    private bool _NextSizeMatcher(int i, out int next, out SizeMatcher o) {
        o = null!;

        var rangeSpecifier = SizeMatcher.ComparisonType.Invalid;
        if (_NextValidCodepoint(i, out next, out var c) && c is '<' or '=' or '>') {
            i = next;
            if (c is '<' or '>' && _NextValidCodepoint(i, out next, out var c2) && c2 is '=') {
                rangeSpecifier = c switch {
                    '>' => SizeMatcher.ComparisonType.GreaterThanOrEquals,
                    '<' => SizeMatcher.ComparisonType.LessThanOrEquals,
                    _ => throw new FailFastException("<>"),
                };
            } else {
                rangeSpecifier = c switch {
                    '=' => SizeMatcher.ComparisonType.Equals,
                    '>' => SizeMatcher.ComparisonType.GreaterThan,
                    '<' => SizeMatcher.ComparisonType.LessThan,
                    _ => throw new FailFastException("<=>"),
                };
            }
        }

        var radix = NumberRadix.Dec;
        for (var j = 0; j < 2; j++) {
            if (_NextValidCodepoint(i + j, out next, out c)) {
                var parsedRadix = c switch {
                    'b' or 'B' or 'y' or 'Y' => NumberRadix.Bin,
                    'o' or 'O' => NumberRadix.Oct,
                    'x' or 'X' => NumberRadix.Hex,
                    _ => NumberRadix.Invalid,
                };

                if (parsedRadix != NumberRadix.Invalid) {
                    radix = parsedRadix;
                    i = next;
                    break;
                }

                // when it's 0b..., 0o..., or 0x...
                if (c != '0')
                    break;
            }
        }

        if (!_NextLiteralFloat(i, out next, out var bytes, BitConverter.IsLittleEndian, radix,
                FloatWidth.Double))
            return false;

        i = next;

        var unitMultiplier = 1ul;
        if (_NextValidCodepoint(i, out next, out c)) {
            unitMultiplier = c switch {
                'b' or 'B' => 1ul,
                'k' or 'K' => 1024ul,
                'm' or 'M' => 1024ul * 1024,
                'g' or 'G' => 1024ul * 1024 * 1024,
                't' or 'T' => 1024ul * 1024 * 1024 * 1024,
                _ => 0ul,
            };

            if (unitMultiplier == 0)
                unitMultiplier = 1;
            else {
                i = next;
                if (unitMultiplier > 1) {
                    // deal with "kb" rather than "k"
                    if (!_NextValidCodepoint(i, out next, out c) && c is 'b' or 'B')
                        next = i;
                }
            }
        }

        var n = BitConverter.ToDouble(bytes);
        o = rangeSpecifier switch {
            SizeMatcher.ComparisonType.Invalid =>
                new(n * unitMultiplier - unitMultiplier / 2.0, n * unitMultiplier + unitMultiplier / 2.0),
            SizeMatcher.ComparisonType.Equals => new(n * unitMultiplier, n * unitMultiplier),
            SizeMatcher.ComparisonType.GreaterThanOrEquals => new(n * unitMultiplier, ulong.MaxValue),
            SizeMatcher.ComparisonType.GreaterThan => new(n * unitMultiplier + 1, ulong.MaxValue),
            SizeMatcher.ComparisonType.LessThanOrEquals => new(ulong.MinValue, n * unitMultiplier),
            SizeMatcher.ComparisonType.LessThan => new(ulong.MinValue, n * unitMultiplier - 1),
            _ => throw new FailFastException("rangeSpecifier?"),
        };
        return true;
    }

    private bool _NextHashMatcher(int i, out int next, out HashMatcher o) {
        o = null!;
        if (!_NextLiteralInteger(i, out next, out var bytes, false, BitConverter.IsLittleEndian, NumberRadix.Hex,
                IntegerWidth.Dword))
            return false;

        o = new(BitConverter.ToUInt32(bytes));
        return true;
    }

    private bool _NextTextMatcher(int i, out int next, out TextMatcher o,
        params uint[] extraTerminatorsIfUnescaped) {
        o = null!;

        // options terminator
        var optionsEscapeTerminator = 0u;
        if (_NextValidCodepoint(i, out next, out var c)) {
            var newOptionTerminator = c switch {
                '[' => ']',
                '(' => ')',
                _ => 0u,
            };
            if (newOptionTerminator != 0u) {
                i = next;
                optionsEscapeTerminator = newOptionTerminator;
            }
        }

        // parse options
        var equalityType = TextMatcher.SearchEqualityType.Contains;
        var matchType = TextMatcher.SearchMatchType.Wildcard;
        var noEscape = false;
        var negate = false;
        for (; _NextValidCodepoint(i, out next, out c); i = next) {
            if (optionsEscapeTerminator == 0u && extraTerminatorsIfUnescaped.Contains(c))
                return false;

            var newEqualityType = c switch {
                'c' when optionsEscapeTerminator != 0u => TextMatcher.SearchEqualityType.Contains,
                'm' when optionsEscapeTerminator != 0u => TextMatcher.SearchEqualityType.Equals,
                's' when optionsEscapeTerminator != 0u => TextMatcher.SearchEqualityType.StartsWith,
                'e' when optionsEscapeTerminator != 0u => TextMatcher.SearchEqualityType.EndsWith,
                '=' => TextMatcher.SearchEqualityType.Equals,
                '<' or '^' => TextMatcher.SearchEqualityType.StartsWith,
                '>' or '$' => TextMatcher.SearchEqualityType.EndsWith,
                _ => TextMatcher.SearchEqualityType.Invalid,
            };
            if (newEqualityType != TextMatcher.SearchEqualityType.Invalid) {
                equalityType = newEqualityType;
                continue;
            }

            var newMatchType = c switch {
                'w' when optionsEscapeTerminator != 0u => TextMatcher.SearchMatchType.Wildcard,
                'x' when optionsEscapeTerminator != 0u => TextMatcher.SearchMatchType.Regex,
                'p' or 't' when optionsEscapeTerminator != 0u => TextMatcher.SearchMatchType.PlainText,
                ':' => TextMatcher.SearchMatchType.PlainText,
                '*' or '?' => TextMatcher.SearchMatchType.Wildcard,
                '/' or '%' => TextMatcher.SearchMatchType.Regex,
                _ => TextMatcher.SearchMatchType.Invalid,
            };
            if (newMatchType != TextMatcher.SearchMatchType.Invalid) {
                matchType = newMatchType;
                continue;
            }

            if (c == '@' || (c == 'r' && optionsEscapeTerminator != 0u)) {
                noEscape = true;
                continue;
            }

            if (c is '!' or '~') {
                negate = !negate;
                continue;
            }

            if (optionsEscapeTerminator == 0u)
                break;

            if (optionsEscapeTerminator == c) {
                i = next;
                break;
            }
        }

        if (!_NextLiteralString(i, out next, out var str, !noEscape, extraTerminatorsIfUnescaped))
            return false;

        o = new(equalityType, matchType, negate, str);
        return true;
    }

    private bool _NextTextFallbackMatcher(int i, out int next, out TextMatcher o, out bool containsPathSeparator, 
        params uint[] extraTerminatorsIfUnescaped) {
        containsPathSeparator = false;
        o = null!;

        if (!_NextLiteralString(i, out next, out var str, true, extraTerminatorsIfUnescaped))
            return false;

        containsPathSeparator = str.Contains('/');

        if (str.Contains('*'))
            o = new(TextMatcher.SearchEqualityType.Equals, TextMatcher.SearchMatchType.Wildcard, false, str);
        else if (str.Contains('?'))
            o = new(TextMatcher.SearchEqualityType.Equals, TextMatcher.SearchMatchType.Wildcard, false, str);
        else
            o = new(TextMatcher.SearchEqualityType.Contains, TextMatcher.SearchMatchType.PlainText, false, str);
        return true;
    }

    private bool _NextLiteralString(int i, out int next, out string s, bool useEscapeSequence,
        params uint[] extraTerminatorsIfUnescaped) {
        var sb = _stringBuilderPool.Get();
        try {
            var inQuote = 0u;

            for (; _NextValidCodepoint(i, out next, out var c); i = next) {
                if (c.IsWhiteSpace() || extraTerminatorsIfUnescaped.Contains(c))
                    break;

                switch (c) {
                    case '"' or '\'' when inQuote == 0:
                        inQuote = c;
                        break;
                    case '"' when inQuote == '"':
                    case '\'' when inQuote == '\'':
                        inQuote = 0u;
                        break;
                    case '\\' when useEscapeSequence:
                        i = next;
                        if (!_NextValidCodepoint(i, out next, out c))
                            break;

                        var isLittleEndian = true;
                        if (c is '<' or '>') {
                            isLittleEndian = c == '<';
                            i = next;
                            if (!_NextValidCodepoint(i, out next, out c))
                                break;
                        }

                        byte[] bytes;
                        int next2;
                        switch (c) {
                            case '=' when _NextLiteralBase64(next, out next2, out bytes):
                            case '@' when _NextLiteralFormatted(next, out next2, out bytes, extraTerminatorsIfUnescaped):
                            case >= '0' and <= '7' when _NextLiteralInteger(next, out next2, out bytes, false,
                                isLittleEndian, NumberRadix.Oct, IntegerWidth.Byte):
                            case 'x' when _NextLiteralInteger(next, out next2, out bytes, false, isLittleEndian,
                                NumberRadix.Hex, IntegerWidth.Byte):
                            case 'u' when _NextLiteralInteger(next, out next2, out bytes, false, isLittleEndian,
                                NumberRadix.Hex, IntegerWidth.Word):
                            case 'U' when _NextLiteralInteger(next, out next2, out bytes, false, isLittleEndian,
                                NumberRadix.Hex, IntegerWidth.Dword):
                                sb.EnsureCapacity(sb.Length + bytes.Length);
                                foreach (var b in bytes)
                                    sb.Append((char)b);
                                next = next2;
                                break;
                            case >= 0x100:
                                bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) c));
                                sb.EnsureCapacity(sb.Length + bytes.Length);
                                foreach (var b in bytes)
                                    sb.Append((char)b);
                                break;
                            default:
                                sb.Append((char) (c switch {
                                    'a' => '\a',
                                    'b' => '\b',
                                    'e' => 0x1b,
                                    'f' => '\f',
                                    'n' => '\n',
                                    'r' => '\r',
                                    't' => '\t',
                                    'v' => '\v',
                                    _ => (char) c,
                                }));
                                break;
                        }

                        break;
                    default:
                        bytes = Encoding.UTF8.GetBytes(char.ConvertFromUtf32((int) c));
                        sb.EnsureCapacity(sb.Length + bytes.Length);
                        foreach (var b in bytes)
                            sb.Append((char)b);
                        break;
                }
            }

            next = i;
            s = sb.ToString();
            return s.Any();
        } finally {
            _stringBuilderPool.Return(sb);
        }
    }

    private bool _NextLiteralInteger(int i, out int next, out byte[] bytes, bool signed, bool isLittleEndian,
        NumberRadix radix, IntegerWidth width) {
        bytes = null!;

        var numBits = (int) width * 8;

        var bitsPerCharacter = radix switch {
            NumberRadix.Bin => 1,
            NumberRadix.Oct => 3,
            NumberRadix.Dec => -1, // let it overflow
            NumberRadix.Hex => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(radix), radix, null),
        };

        next = i;
        var readBits = 0;
        var value = 0ul;
        uint d;

        var signedMultiplier = 1;
        if (signed) {
            if (_NextDigit(i, out next, out d, radix)) {
                i = next;
                value = d;
                readBits++;
            } else if (_NextValidCodepoint(i, out next, out var c) && c is '-' or '+') {
                i = next;
                if (c == '-')
                    signedMultiplier = -1;
            }
        }

        for (; readBits < numBits && _NextDigit(i, out next, out d, radix); readBits += bitsPerCharacter, i = next)
            value = value * (uint) radix + d;

        if (readBits == 0)
            return false;

        bytes = signed
            ? width switch {
                IntegerWidth.Byte => new[] {unchecked((byte) (sbyte) (signedMultiplier * (long) value))},
                IntegerWidth.Word => BitConverter.GetBytes(unchecked((short) (signedMultiplier * (long) value))),
                IntegerWidth.Dword => BitConverter.GetBytes(unchecked((int) (signedMultiplier * (long) value))),
                IntegerWidth.Qword => BitConverter.GetBytes(signedMultiplier * (long) value),
                _ => throw new ArgumentOutOfRangeException(nameof(width), width, null),
            }
            : width switch {
                IntegerWidth.Byte => new[] {unchecked((byte) value)},
                IntegerWidth.Word => BitConverter.GetBytes(unchecked((ushort) value)),
                IntegerWidth.Dword => BitConverter.GetBytes(unchecked((uint) value)),
                IntegerWidth.Qword => BitConverter.GetBytes(value),
                _ => throw new ArgumentOutOfRangeException(nameof(width), width, null),
            };

        if (isLittleEndian != BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        next = i;
        return true;
    }

    private bool _NextLiteralFloat(int i, out int next, out byte[] bytes, bool isLittleEndian, NumberRadix radix,
        FloatWidth width) {
        bytes = null!;

        next = i;
        uint c;
        var value = 0.0;
        var len = 0;

        var signedMultiplier = 1;
        if (_NextDigit(i, out next, out var d, radix)) {
            i = next;
            value = d;
            len++;
        } else if (_NextValidCodepoint(i, out next, out c)) {
            i = next;
            switch (c) {
                case '+':
                    break;
                case '-':
                    signedMultiplier = -1;
                    break;
                default:
                    return false;
            }
        }

        for (; _NextDigit(i, out next, out d, radix); i = next, len++)
            value = value * (uint) radix + d;

        if (_NextValidCodepoint(i, out next, out c)
            && c == Thread.CurrentThread.CurrentUICulture.NumberFormat.NumberDecimalSeparator[0]) {
            i = next;
            var rationalMultiplier = 0.1;
            for (; _NextDigit(i, out next, out d, radix); i = next, rationalMultiplier /= (uint) radix, len++)
                value += d * rationalMultiplier;
        }

        if (len == 0)
            return false;

        value *= signedMultiplier;

        bytes = width switch {
            FloatWidth.Half => BitConverter.GetBytes((Half) value),
            FloatWidth.Float => BitConverter.GetBytes((float) value),
            FloatWidth.Double => BitConverter.GetBytes(value),
            _ => throw new ArgumentOutOfRangeException(nameof(width), width, null),
        };

        if (isLittleEndian != BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        next = i;
        return true;
    }

    private bool _NextLiteralBase64(int i, out int next, out byte[] bytes) {
        bytes = null!;

        var sb = _stringBuilderPool.Get();
        try {
            for (; _NextValidCodepoint(i, out next, out var c); i = next) {
                sb.Append(c switch {
                    '=' when sb.Length % 4 != 0 => '=',
                    >= 'a' and <= 'z' => c,
                    >= 'A' and <= 'Z' => c,
                    >= '0' and <= '9' => c,
                    '+' or '/' => c,
                    '-' => '+',
                    '_' => '/',
                    _ => 0,
                });

                if (sb[^1] == 0) {
                    sb.Remove(sb.Length - 1, 1);
                    break;
                }
            }

            if (sb.Length % 4 != 0)
                sb.Append('=', 4 - sb.Length % 4);

            if (sb.Length == 0)
                return false;

            bytes = Convert.FromBase64String(sb.ToString());
            next = i;
            return true;
        } finally {
            _stringBuilderPool.Return(sb);
        }
    }

    private bool _NextLiteralFormatted(int i, out int next, out byte[] bytes, uint[] extraTerminatorsIfUnescaped) {
        var isLittleEndian = true;
        if (_NextValidCodepoint(i, out next, out var c) && c is '<' or '>') {
            isLittleEndian = c == '<';
            i = next;
        }

        var radix = NumberRadix.Hex;
        if (_NextValidCodepoint(i, out next, out c)) {
            var parsedRadix = c switch {
                'b' or 'B' or 'y' or 'Y' => NumberRadix.Bin,
                'o' or 'O' => NumberRadix.Oct,
                'x' or 'X' => NumberRadix.Hex,
                _ => NumberRadix.Invalid,
            };

            if (parsedRadix != NumberRadix.Invalid) {
                radix = parsedRadix;
                i = next;
            }
        }

        var intWidth = IntegerWidth.Byte;
        var floatWidth = FloatWidth.Invalid;
        var signed = true;
        // https://docs.python.org/3/library/struct.html#format-characters
        if (_NextValidCodepoint(i, out next, out c)) {
            if (c switch {
                    'b' or 'c' => Tuple.Create(IntegerWidth.Byte, FloatWidth.Invalid, true),
                    'B' or 'C' => Tuple.Create(IntegerWidth.Byte, FloatWidth.Invalid, false),
                    'h' or 's' => Tuple.Create(IntegerWidth.Word, FloatWidth.Invalid, true),
                    'H' or 'S' => Tuple.Create(IntegerWidth.Word, FloatWidth.Invalid, false),
                    'i' or 'l' => Tuple.Create(IntegerWidth.Dword, FloatWidth.Invalid, true),
                    'I' or 'L' => Tuple.Create(IntegerWidth.Dword, FloatWidth.Invalid, false),
                    'q' or 'n' => Tuple.Create(IntegerWidth.Qword, FloatWidth.Invalid, true),
                    'Q' or 'N' => Tuple.Create(IntegerWidth.Qword, FloatWidth.Invalid, false),
                    'e' or 'E' => Tuple.Create(IntegerWidth.Invalid, FloatWidth.Half, true),
                    'f' or 'F' => Tuple.Create(IntegerWidth.Invalid, FloatWidth.Float, true),
                    'd' or 'D' => Tuple.Create(IntegerWidth.Invalid, FloatWidth.Double, true),
                    _ => null,
                } is { } parsedTuple) {
                (intWidth, floatWidth, signed) = parsedTuple;
                i = next;
            }
        }

        var arrayTerminator = 0u;
        if (_NextValidCodepoint(i, out next, out c)) {
            if (c switch {
                    '[' => ']',
                    '(' => ')',
                    '{' => '}',
                    '<' => '>',
                    _ => (char?) null,
                } is { } parsedArrayTerminator) {
                arrayTerminator = parsedArrayTerminator;
                i = next;
            }
        }

        var ms = _memoryStreamPool.Get();
        try {
            while (true) {
                if (intWidth != IntegerWidth.Invalid) {
                    if (!_NextLiteralInteger(i, out next, out var b, signed, isLittleEndian, radix, intWidth))
                        break;
                    ms.Write(b);
                    i = next;
                } else if (floatWidth != FloatWidth.Invalid) {
                    if (!_NextLiteralFloat(i, out next, out var b, isLittleEndian, radix, floatWidth))
                        break;
                    ms.Write(b);
                    i = next;
                } else
                    throw new FailFastException("Must not reach here");

                if (arrayTerminator == 0)
                    break;

                if (!_NextValidCodepoint(i, out next, out c))
                    break;

                if (extraTerminatorsIfUnescaped.Contains(c))
                    break;

                if (c == arrayTerminator)
                    break;

                next = i;
                while (!_NextDigit(i, out next, out var d, radix))
                    i = next;
            }

            bytes = ms.Length == 0 ? null! : ms.ToArray();
            return bytes != null!;
        } finally {
            _memoryStreamPool.Return(ms);
        }
    }

    private bool _NextDigit(int i, out int next, out uint d, NumberRadix radix, bool ignoreUnderscores = true) {
        d = uint.MaxValue;

        while (_NextValidCodepoint(i, out next, out var c)) {
            switch (c) {
                case < '0':
                    return false;
                case '_' when ignoreUnderscores:
                    continue;
                case '0' or '1':
                case <= '7' when radix >= NumberRadix.Oct:
                case <= '9' when radix >= NumberRadix.Dec:
                    d = c - '0';
                    return true;
                case >= 'a' and <= 'f' when radix >= NumberRadix.Hex:
                    d = 10 + c - 'a';
                    return true;
                case >= 'A' and <= 'F' when radix >= NumberRadix.Hex:
                    d = 10 + c - 'A';
                    return true;
                case >= 'ａ' and <= 'ｆ' when radix >= NumberRadix.Hex:
                    d = 10 + c - 'ａ';
                    return true;
                case >= 'Ａ' and <= 'Ｆ' when radix >= NumberRadix.Hex:
                    d = 10 + c - 'Ａ';
                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    public bool DrainWhitespaces(ref int i) => _NextWhitespace(i, out i);

    private bool _NextWhitespace(int i, out int next) {
        var count = 0;
        for (; _NextValidCodepoint(i, out next, out var c); i = next, count++) {
            if (!c.IsWhiteSpace())
                break;
        }

        next = i;
        return count > 0;
    }

    private bool _NextExactLiteral(int i, out int next, string literal, int minLength, bool ignoreCase,
        bool mustEndWithTerminator, params uint[] terminators) {
        uint c;

        foreach (var fc in literal) {
            if (!_NextValidCodepoint(i, out next, out c))
                return false;

            minLength--;
            if (terminators.Contains(c) && minLength <= 0)
                return true;

            if (c != fc &&
                (!ignoreCase || (c < 0x10000 && char.ToLowerInvariant((char) c) != char.ToLowerInvariant(fc))))
                return false;

            i = next;
        }

        if (_NextValidCodepoint(i, out next, out c)) {
            if (terminators.Contains(c))
                return true;
        }

        next = i;
        return !mustEndWithTerminator;
    }

    private bool _NextValidCodepoint(int i, out int next, out uint c) {
        for (; i < _query.Length; i++) {
            switch (_query[i] & 0xFC00) {
                case 0xD800:
                    if (i + 1 >= _query.Length || (_query[i + 1] & 0xFC00) != 0xDC00) {
                        // invalid; surrogate #2 must appear here.
                        continue;
                    }

                    next = i + 2;
                    c = 0x10000u + ((_query[i] & 0x3FFu) << 10 | (_query[i + 1] & 0x3FFu));
                    return true;
                case 0xDC00:
                    // invalid; surrogate #2 cannot appear here.
                    continue;
                default:
                    next = i + 1;
                    c = _query[i];
                    return true;
            }
        }

        next = i;
        c = uint.MaxValue;
        return false;
    }

    public enum NumberRadix : uint {
        Bin = 2,
        Oct = 8,
        Dec = 10,
        Hex = 16,
        Invalid = uint.MaxValue,
    }

    public enum IntegerWidth {
        Byte = 1,
        Word = 2,
        Dword = 4,
        Qword = 8,
        Invalid = int.MaxValue,
    }

    public enum FloatWidth {
        Half = 2,
        Float = 4,
        Single = Float,
        Double = 8,
        Invalid = int.MaxValue,
    }
}
