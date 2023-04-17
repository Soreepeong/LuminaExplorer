using System.Diagnostics;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class SingleConditionMatcher : IMatcher {
    private readonly MatchWhat _matchWhat;
    private readonly TypeConstraint? _typeConstraint;
    private readonly TextMatcher? _textMatcher;
    private readonly HashMatcher? _hashMatcher;
    private readonly SizeMatcher? _sizeMatcher;

    public SingleConditionMatcher(TypeConstraint typeConstraint) {
        _matchWhat = MatchWhat.Type;
        _typeConstraint = typeConstraint;
    }

    public SingleConditionMatcher(MatchWhat what, TextMatcher matcher) {
        _matchWhat = what;
        _textMatcher = matcher;
    }

    public SingleConditionMatcher(HashMatcher matcher) {
        _matchWhat = MatchWhat.Hash;
        _hashMatcher = matcher;
    }

    public SingleConditionMatcher(MatchWhat what, SizeMatcher matcher) {
        _matchWhat = what;
        _sizeMatcher = matcher;
    }

    public bool Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout) =>
        _matchWhat switch {
            MatchWhat.Path => _textMatcher!.Matches(tree.GetFullPath(folder), stopwatch, timeout),
            MatchWhat.Data => false,
            MatchWhat.Type => _typeConstraint is TypeConstraint.NoConstraint or TypeConstraint.Folder,
            MatchWhat.Hash => _hashMatcher!.Matches(folder.FolderHash),
            MatchWhat.RawSize => false,
            MatchWhat.OccupiedSize => false,
            MatchWhat.ReservedSize => false,
            _ => throw new InvalidOperationException()
        };

    public bool Matches(VirtualSqPackTree tree, VirtualFile file, ref VirtualFileLookup? lookup, Lazy<string> data,
        Stopwatch stopwatch, TimeSpan timeout) =>
        _matchWhat switch {
            MatchWhat.Path => _textMatcher!.Matches(tree.GetFullPath(file), stopwatch, timeout),
            MatchWhat.Data => _textMatcher!.Matches(data.Value, stopwatch, timeout),
            MatchWhat.Type => _typeConstraint is TypeConstraint.NoConstraint or TypeConstraint.Folder,
            MatchWhat.Hash => _hashMatcher!.Matches(file.FileHash) || _hashMatcher!.Matches(tree.GetFullPathHash(file)),
            MatchWhat.RawSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).Size),
            MatchWhat.OccupiedSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).OccupiedBytes),
            MatchWhat.ReservedSize => _sizeMatcher!.Matches((lookup ??= tree.GetLookup(file)).ReservedBytes),
            _ => throw new InvalidOperationException()
        };

    public IMatcher UnwrapIfPossible() => this;

    public override string ToString() => _matchWhat switch {
        MatchWhat.Path => $"Path:{_textMatcher}",
        MatchWhat.Data => $"Data:{_textMatcher}",
        MatchWhat.Type => $"Type:{_typeConstraint}",
        MatchWhat.Hash => $"Hash:{_hashMatcher}",
        MatchWhat.RawSize => $"RawSize:{_sizeMatcher}",
        MatchWhat.OccupiedSize => $"OccupiedSize:{_sizeMatcher}",
        MatchWhat.ReservedSize => $"ReservedSize:{_sizeMatcher}",
        _ => $"SingleConditionMatcher({_matchWhat})",
    };

    public enum MatchWhat {
        Path,
        Data,
        Type,
        Hash,
        RawSize,
        OccupiedSize,
        ReservedSize,
    }

    public enum TypeConstraint {
        NoConstraint,
        Folder,
        File,
        Empty,
        Standard,
        Texture,
        Model,
    }
}
