using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class SingleConditionMatcher : IMatcher {
    private MatchWhat? _matchWhat;
    private TypeConstraint? _typeConstraint;
    private TextMatcher? _textMatcher;
    private HashMatcher? _hashMatcher;
    private SizeMatcher? _sizeMatcher;

    public void ParseQuery(Span<uint> span, ref int i, uint[] validTerminators) {
        _matchWhat = null;
        _typeConstraint = null;

        _textMatcher = null;
        _hashMatcher = null;
        _sizeMatcher = null;

        for (; i < span.Length; i++) {
            if (validTerminators.Contains(span[i]))
                break;

            if (!IsEmpty())
                continue;

            uint[] matchedArray;
            if (span[i..].StartsWith(matchedArray = new uint[] {'p', 'a', 't', 'h', ':'})) {
                i += matchedArray.Length;
                _textMatcher = new();
                _textMatcher.ParseQuery(span, ref i, validTerminators);
                if (_textMatcher.IsEmpty())
                    _textMatcher = null;
                else
                    _matchWhat = MatchWhat.Path;
            } else if (span[i..].StartsWith(matchedArray = new uint[] {'t', 'y', 'p', 'e', ':'})) {
                i += matchedArray.Length;

                if (i >= span.Length)
                    continue;
                _typeConstraint = span[i] switch {
                    'F' or 'f' when i + 1 < span.Length => span[++i] switch {
                        'O' or 'o' => TypeConstraint.Folder,
                        'I' or 'i' => TypeConstraint.File,
                        _ => _typeConstraint,
                    },
                    'D' or 'd' => TypeConstraint.Folder,
                    'E' or 'e' => TypeConstraint.Empty,
                    'S' or 's' or 'B' or 'b' => TypeConstraint.Standard,
                    'T' or 't' => TypeConstraint.Texture,
                    'M' or 'm' => TypeConstraint.Model,
                    _ => _typeConstraint,
                };

                if (_typeConstraint != null)
                    _matchWhat = MatchWhat.Type;
            } else if (span[i..].StartsWith(matchedArray = new uint[] {'h', 'a', 's', 'h', ':'})) {
                i += matchedArray.Length;
                _hashMatcher = new();
                _hashMatcher.ParseQuery(span, ref i, validTerminators);
                if (_hashMatcher.IsEmpty())
                    _hashMatcher = null;
                else
                    _matchWhat = MatchWhat.Hash;
            } else if (span[i..].StartsWith(matchedArray = new uint[] {'s', 'i', 'z', 'e', ':'}) ||
                       span[i..].StartsWith(matchedArray = new uint[] {'l', 'e', 'n', ':'}) ||
                       span[i..].StartsWith(matchedArray = new uint[] {'l', 'e', 'n', 'g', 't', 'h', ':'})) {
                i += matchedArray.Length;
                _sizeMatcher = new();
                _sizeMatcher.ParseQuery(span, ref i, validTerminators);
                if (_sizeMatcher.IsEmpty())
                    _sizeMatcher = null;
                else
                    _matchWhat = MatchWhat.RawSize;
            } else if (span[i..].StartsWith(matchedArray = new uint[] {'o', 'c', 'c', 'u', 'p', 'i', 'e', 'd', ':'})) {
                i += matchedArray.Length;
                _sizeMatcher = new();
                _sizeMatcher.ParseQuery(span, ref i, validTerminators);
                if (_sizeMatcher.IsEmpty())
                    _sizeMatcher = null;
                else
                    _matchWhat = MatchWhat.OccupiedSize;
            } else if (span[i..].StartsWith(matchedArray = new uint[] {'r', 'e', 's', 'e', 'r', 'v', 'e', 'd', ':'})) {
                i += matchedArray.Length;
                _sizeMatcher = new();
                _sizeMatcher.ParseQuery(span, ref i, validTerminators);
                if (_sizeMatcher.IsEmpty())
                    _sizeMatcher = null;
                else
                    _matchWhat = MatchWhat.ReservedSize;
            } else {
                _textMatcher = new();
                _textMatcher.ParseQuery(span, ref i, validTerminators);
                if (_textMatcher.IsEmpty())
                    _textMatcher = null;
                else
                    _matchWhat = MatchWhat.Path;
            }
        }
    }

    public bool IsEmpty() => _matchWhat is null;

    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout) =>
        _matchWhat switch {
            null => throw new InvalidOperationException(),
            MatchWhat.Path => _textMatcher!.Matches(tree.GetFullPath(folder), stopwatch, timeout),
            MatchWhat.Type => _typeConstraint is TypeConstraint.NoConstraint or TypeConstraint.Folder,
            MatchWhat.Hash => _hashMatcher!.Matches(folder.FolderHash),
            MatchWhat.RawSize => false,
            MatchWhat.OccupiedSize => false,
            MatchWhat.ReservedSize => false,
            _ => throw new InvalidOperationException()
        };

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Stopwatch stopwatch, TimeSpan timeout) =>
        _matchWhat switch {
            null => throw new InvalidOperationException(),
            MatchWhat.Path => _textMatcher!.Matches(tree.GetFullPath(file), stopwatch, timeout),
            MatchWhat.Type => _typeConstraint is TypeConstraint.NoConstraint or TypeConstraint.Folder,
            MatchWhat.Hash => _hashMatcher!.Matches(file.FileHash) || _hashMatcher!.Matches(tree.GetFullPathHash(file)),
            MatchWhat.RawSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).Size),
            MatchWhat.OccupiedSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).OccupiedBytes),
            MatchWhat.ReservedSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).ReservedBytes),
            _ => throw new InvalidOperationException()
        };

    private enum MatchWhat {
        Path,
        Type,
        Hash,
        RawSize,
        OccupiedSize,
        ReservedSize,
    }

    private enum TypeConstraint {
        NoConstraint,
        Folder,
        File,
        Empty,
        Standard,
        Texture,
        Model,
    }
}
