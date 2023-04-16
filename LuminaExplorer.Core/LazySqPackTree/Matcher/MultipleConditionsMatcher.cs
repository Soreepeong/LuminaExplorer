using System.Diagnostics;
using System.Text;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class MultipleConditionsMatcher : IMatcher {
    private static readonly uint[] ValidWhitespaces = {
        '\0',
        // https://learn.microsoft.com/en-us/dotnet/api/system.char.iswhitespace
        0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020, 0x0085, 0x00A0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003,
        0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000,
    };
    
    private static readonly uint[] TerminatorWithClosingParenthesis = {
        '\0', ')',
        // https://learn.microsoft.com/en-us/dotnet/api/system.char.iswhitespace
        0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x0020, 0x0085, 0x00A0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003,
        0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A, 0x2028, 0x2029, 0x202F, 0x205F, 0x3000,
    };
    
    private IMatcher? _matcher1;
    private IMatcher? _matcher2;
    private OperatorType? _operator = null;

    public void ParseQuery(string query) {
        var b32 = Encoding.UTF32.GetBytes(query);
        unsafe {
            fixed (byte* pb32 = b32) {
                var i = 0;
                ParseQuery(new(pb32, b32.Length / 4), ref i, Array.Empty<uint>());
            }
        }
    }

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        _matcher1 = _matcher2 = null;
        _operator = null;

        for (; i < span.Length; i++) {
            SkipWhitespaces(span, ref i);
            if (i >= span.Length)
                break;
            
            if (validTerminators.Contains(span[i]))
                break;

            uint[] matchedArray;
            if (_matcher1 is null && span[i] == '(') {
                i++;
                _matcher1 = new MultipleConditionsMatcher();
                _matcher1.ParseQuery(span, ref i, new uint[]{')'});
                if (_matcher1.IsEmpty())
                    _matcher1 = null;
                else
                    _operator = OperatorType.Parenthesized;
            } else if (
                _matcher1 is null &&
                span[i..].StartsWith(matchedArray = new uint[] {'N', 'O', 'T'}) &&
                i + matchedArray.Length < span.Length &&
                span[i + matchedArray.Length].IsWhiteSpace()) {
                i += matchedArray.Length;
                SkipWhitespaces(span, ref i);
                _matcher1 = new SingleConditionMatcher();
                _matcher1.ParseQuery(span, ref i, ValidWhitespaces.Concat(validTerminators).ToArray());
                if (_matcher1.IsEmpty())
                    _matcher1 = null;
                else
                    _operator = OperatorType.Not;
            } else if (
                _matcher1 is not null &&
                span[i..].StartsWith(matchedArray = new uint[] {'A', 'N', 'D'}) &&
                i + matchedArray.Length < span.Length &&
                span[i + matchedArray.Length].IsWhiteSpace()) {
                i += matchedArray.Length;
                SkipWhitespaces(span, ref i);
                _matcher2 = new MultipleConditionsMatcher();
                _matcher2.ParseQuery(span, ref i, validTerminators);
                if (_matcher2.IsEmpty())
                    _matcher2 = null;
                else
                    _operator = OperatorType.And;
            } else if (
                _matcher1 is not null &&
                span[i..].StartsWith(matchedArray = new uint[] {'O', 'R'}) &&
                i + matchedArray.Length < span.Length &&
                span[i + matchedArray.Length].IsWhiteSpace()) {
                i += matchedArray.Length;
                SkipWhitespaces(span, ref i);
                _matcher2 = new MultipleConditionsMatcher();
                _matcher2.ParseQuery(span, ref i, validTerminators);
                if (_matcher2.IsEmpty())
                    _matcher2 = null;
                else
                    _operator = OperatorType.Or;
            } else if (
                _matcher1 is not null &&
                span[i..].StartsWith(matchedArray = new uint[] {'X', 'O', 'R'}) &&
                i + matchedArray.Length < span.Length &&
                span[i + matchedArray.Length].IsWhiteSpace()) {
                i += matchedArray.Length;
                SkipWhitespaces(span, ref i);
                _matcher2 = new MultipleConditionsMatcher();
                _matcher2.ParseQuery(span, ref i, validTerminators);
                if (_matcher2.IsEmpty())
                    _matcher2 = null;
                else
                    _operator = OperatorType.Xor;
            } else if (_matcher1 is null) {
                _matcher1 = new SingleConditionMatcher();
                _matcher1.ParseQuery(span, ref i, ValidWhitespaces.Concat(validTerminators).ToArray());
                if (_matcher1.IsEmpty())
                    _matcher1 = null;
                else
                    _operator = OperatorType.Single;
            } else if (_matcher2 is null) {
                _matcher2 = new MultipleConditionsMatcher();
                _matcher2.ParseQuery(span, ref i, validTerminators);
                if (_matcher2.IsEmpty())
                    _matcher2 = null;
                else
                    _operator = OperatorType.And;
            } else
                continue;

            i--;
        }
    }

    public bool IsEmpty() => _matcher1 is null || _operator is null;

    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout) =>
        _operator switch {
            null => throw new InvalidOperationException(),
            OperatorType.Single => _matcher1!.Matches(tree, folder, stopwatch, timeout),
            OperatorType.Parenthesized => _matcher1!.Matches(tree, folder, stopwatch, timeout),
            OperatorType.Not => !_matcher1!.Matches(tree, folder, stopwatch, timeout),
            OperatorType.And => _matcher1!.Matches(tree, folder, stopwatch, timeout) &&
                                _matcher2!.Matches(tree, folder, stopwatch, timeout),
            OperatorType.Or => _matcher1!.Matches(tree, folder, stopwatch, timeout) ||
                               _matcher2!.Matches(tree, folder, stopwatch, timeout),
            OperatorType.Xor => _matcher1!.Matches(tree, folder, stopwatch, timeout) ^
                                _matcher2!.Matches(tree, folder, stopwatch, timeout),
            _ => throw new InvalidOperationException()
        };

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Lazy<string> data, Stopwatch stopwatch,
        TimeSpan timeout) => _operator switch {
            null => throw new InvalidOperationException(),
            OperatorType.Single => _matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            OperatorType.Parenthesized => _matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            OperatorType.Not => !_matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            OperatorType.And => _matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout) &&
                                _matcher2!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            OperatorType.Or => _matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout) ||
                               _matcher2!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            OperatorType.Xor => _matcher1!.Matches(tree, file, ref lookup, data, stopwatch, timeout) ^
                                _matcher2!.Matches(tree, file, ref lookup, data, stopwatch, timeout),
            _ => throw new InvalidOperationException()
        };

    public override string ToString() => _operator switch {
        OperatorType.Single => $"{_matcher1}",
        OperatorType.Parenthesized => $"({_matcher1})",
        OperatorType.Not => $"!{_matcher1}",
        OperatorType.And => $"{_matcher1} && {_matcher2}",
        OperatorType.Or => $"{_matcher1} || {_matcher2}",
        OperatorType.Xor => $"{_matcher1} ^ {_matcher2}",
        _ => $"MultipleConditionsMatcher({_operator})"
    };

    private static void SkipWhitespaces(Span<uint> span, ref int i) {
        for (; i < span.Length; i++) {
            if (!ValidWhitespaces.Contains(span[i]))
                break;
        }
    }

    private enum OperatorType {
        Single,
        Parenthesized,
        Not,
        And,
        Or,
        Xor,
    }
}

/*
MultipleConditionsMatcher:
	'(' MultipleConditionsMatcher ')'
	| 'NOT' SingleConditionMatcher
	| SingleConditionMatcher 'AND' MultipleConditionsMatcher
	| SingleConditionMatcher 'OR' MultipleConditionsMatcher
	| SingleConditionMatcher 'XOR' MultipleConditionsMatcher
	| SingleConditionMatcher MultipleConditionsMatcher
*/
