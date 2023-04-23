using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree.Matcher;

public class SingleConditionMatcher : IMatcher {
    private readonly MatchWhat _matchWhat;
    private readonly TypeConstraint _typeConstraint = TypeConstraint.Invalid;
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

    public Task<bool> Matches(VirtualSqPackTree tree, VirtualFolder folder, Stopwatch stopwatch, TimeSpan timeout,
        CancellationToken cancellationToken) =>
        _matchWhat switch {
            MatchWhat.Name => _textMatcher!.Matches(folder.Name, stopwatch, timeout, cancellationToken),
            MatchWhat.Path => _textMatcher!.Matches(tree.GetFullPath(folder), stopwatch, timeout, cancellationToken),
            MatchWhat.Data => Task.FromResult(false),
            MatchWhat.Type => Task.FromResult(_typeConstraint is TypeConstraint.Invalid or TypeConstraint.Folder),
            MatchWhat.Hash => _hashMatcher!.Matches(folder.FolderHash, cancellationToken),
            MatchWhat.RawSize => Task.FromResult(false),
            MatchWhat.OccupiedSize => Task.FromResult(false),
            MatchWhat.ReservedSize => Task.FromResult(false),
            _ => throw new InvalidOperationException(),
        };

    public async Task<bool> Matches(VirtualSqPackTree tree, VirtualFile file, Lazy<VirtualFileLookup> lookup,
        Task<Task<string>> data,
        Stopwatch stopwatch, TimeSpan timeout, CancellationToken cancellationToken) {
        return _matchWhat switch {
            MatchWhat.Name => await _textMatcher!.Matches(file.Name, stopwatch, timeout, cancellationToken),
            MatchWhat.Path => await _textMatcher!.Matches(tree.GetFullPath(file), stopwatch, timeout, cancellationToken),
            MatchWhat.Data => await _textMatcher!.Matches(await (await data.AsStarted()), stopwatch, timeout, cancellationToken),
            MatchWhat.Type => _typeConstraint is TypeConstraint.File ||
                              TypeMatches(_typeConstraint, lookup.Value.Type),
            MatchWhat.Hash => await _hashMatcher!.Matches(file.FileHash, cancellationToken)
                              || await _hashMatcher!.Matches(tree.GetFullPathHash(file), cancellationToken),
            MatchWhat.RawSize => await _sizeMatcher!.Matches(lookup.Value.Size, cancellationToken),
            MatchWhat.OccupiedSize => await _sizeMatcher!.Matches(lookup.Value.OccupiedBytes, cancellationToken),
            MatchWhat.ReservedSize => await _sizeMatcher!.Matches(lookup.Value.ReservedBytes, cancellationToken),
            _ => throw new InvalidOperationException(),
        };
    }

    public IMatcher UnwrapIfPossible() => this;

    public override string ToString() => _matchWhat switch {
        MatchWhat.Name => $"Name:{_textMatcher}",
        MatchWhat.Path => $"Path:{_textMatcher}",
        MatchWhat.Data => $"Data:{_textMatcher}",
        MatchWhat.Type => $"Type:{_typeConstraint}",
        MatchWhat.Hash => $"Hash:{_hashMatcher}",
        MatchWhat.RawSize => $"RawSize:{_sizeMatcher}",
        MatchWhat.OccupiedSize => $"OccupiedSize:{_sizeMatcher}",
        MatchWhat.ReservedSize => $"ReservedSize:{_sizeMatcher}",
        _ => $"SingleConditionMatcher({_matchWhat})",
    };

    private static bool TypeMatches(TypeConstraint constraint, FileType type) => constraint switch {
        TypeConstraint.Empty when type == FileType.Empty => true,
        TypeConstraint.Standard when type == FileType.Standard => true,
        TypeConstraint.Model when type == FileType.Model => true,
        TypeConstraint.Texture when type == FileType.Texture => true,
        _ => false,
    };

    public enum MatchWhat {
        Name,
        Path,
        Data,
        Type,
        Hash,
        RawSize,
        OccupiedSize,
        ReservedSize,
    }

    public enum TypeConstraint {
        Folder,
        File,
        Empty,
        Standard,
        Texture,
        Model,
        Invalid = int.MaxValue,
    }
}
