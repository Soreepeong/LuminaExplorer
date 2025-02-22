using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Matcher;

public class SingleConditionMatcher : IMatcher {
    private readonly MatchWhat _matchWhat;
    private readonly TypeConstraint _typeConstraint = TypeConstraint.Invalid;
    private readonly TextMatcher? _textMatcher;
    private readonly HashMatcher? _hashMatcher;
    private readonly SizeMatcher? _sizeMatcher;

    public SingleConditionMatcher(TypeConstraint typeConstraint)
    {
        this._matchWhat = MatchWhat.Type;
        this._typeConstraint = typeConstraint;
    }

    public SingleConditionMatcher(MatchWhat what, TextMatcher matcher)
    {
        this._matchWhat = what;
        this._textMatcher = matcher;
    }

    public SingleConditionMatcher(HashMatcher matcher)
    {
        this._matchWhat = MatchWhat.Hash;
        this._hashMatcher = matcher;
    }

    public SingleConditionMatcher(MatchWhat what, SizeMatcher matcher)
    {
        this._matchWhat = what;
        this._sizeMatcher = matcher;
    }

    public Task<bool> Matches(
        IVirtualFileSystem tree,
        IVirtualFolder folder,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        this._matchWhat switch {
            MatchWhat.Name => this._textMatcher!.Matches(folder.Name, stopwatch, timeout, cancellationToken),
            MatchWhat.Path => this._textMatcher!.Matches(
                tree.GetFullPath(folder),
                stopwatch,
                timeout,
                cancellationToken),
            MatchWhat.Data => Task.FromResult(false),
            MatchWhat.Type => Task.FromResult(this._typeConstraint is TypeConstraint.Invalid or TypeConstraint.Folder),
            MatchWhat.Hash => this._hashMatcher!.Matches(folder.PathHash, cancellationToken),
            MatchWhat.RawSize => Task.FromResult(false),
            MatchWhat.OccupiedSize => Task.FromResult(false),
            MatchWhat.ReservedSize => Task.FromResult(false),
            _ => throw new InvalidOperationException(),
        };

    public async Task<bool> Matches(
        IVirtualFileSystem tree,
        IVirtualFile file,
        Lazy<IVirtualFileLookup> lookup,
        Task<Task<string>> data,
        Stopwatch stopwatch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return this._matchWhat switch {
            MatchWhat.Name => await this._textMatcher!.Matches(file.Name, stopwatch, timeout, cancellationToken),
            MatchWhat.Path => await this._textMatcher!.Matches(
                tree.GetFullPath(file),
                stopwatch,
                timeout,
                cancellationToken),
            MatchWhat.Data => await this._textMatcher!.Matches(
                await (await data.AsStarted()),
                stopwatch,
                timeout,
                cancellationToken),
            MatchWhat.Type => this._typeConstraint is TypeConstraint.File ||
                TypeMatches(this._typeConstraint, lookup.Value.Type),
            MatchWhat.Hash => await this._hashMatcher!.Matches(file.NameHash, cancellationToken)
                || await this._hashMatcher!.Matches(tree.GetFullPathHash(file), cancellationToken),
            MatchWhat.RawSize => await this._sizeMatcher!.Matches(lookup.Value.Size, cancellationToken),
            MatchWhat.OccupiedSize => await this._sizeMatcher!.Matches(lookup.Value.OccupiedBytes, cancellationToken),
            MatchWhat.ReservedSize => await this._sizeMatcher!.Matches(lookup.Value.ReservedBytes, cancellationToken),
            _ => throw new InvalidOperationException(),
        };
    }

    public IMatcher UnwrapIfPossible() => this;

    public override string ToString() =>
        this._matchWhat switch {
            MatchWhat.Name => $"Name:{this._textMatcher}",
            MatchWhat.Path => $"Path:{this._textMatcher}",
            MatchWhat.Data => $"Data:{this._textMatcher}",
            MatchWhat.Type => $"Type:{this._typeConstraint}",
            MatchWhat.Hash => $"Hash:{this._hashMatcher}",
            MatchWhat.RawSize => $"RawSize:{this._sizeMatcher}",
            MatchWhat.OccupiedSize => $"OccupiedSize:{this._sizeMatcher}",
            MatchWhat.ReservedSize => $"ReservedSize:{this._sizeMatcher}",
            _ => $"SingleConditionMatcher({this._matchWhat})",
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
