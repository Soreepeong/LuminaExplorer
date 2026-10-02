using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Lumina;
using Lumina.Data;
using Lumina.Misc;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.Core.SqPackPath;

/// <summary>An existing game file or folder that a text refers to.</summary>
/// <param name="Path">Full path without a leading slash, as known (case may vary); folders end with a slash.</param>
/// <param name="IsFolder">Whether the path is a folder.</param>
public sealed record GamePathMatch(string Path, bool IsFolder);

/// <summary>
/// Tests whether game files and folders exist, and finds the files that partial paths in game data (such as
/// <c>vfx/common/eff/levitate0f</c> or <c>ability/2ax_warrior/abl001</c>) refer to. Thread safe; results are cached.
/// </summary>
public sealed class GamePathResolver {
    /// <summary>Extensions tried for paths without one, after the hinted ones.</summary>
    public static readonly string[] CommonExtensions = [
        "avfx", "scd", "tex", "atex", "mdl", "tmb", "pap", "sgb", "lgb", "lvb", "uld", "cutb", "imc", "mtrl", "sklb",
        "eid", "shpk", "luab", "lua", "exh", "pbd", "est", "phyb", "skp", "uldb", "ulds",
    ];

    private static readonly ConditionalWeakTable<SqpackFileSystem, GamePathResolver> Instances = new();

    private readonly GameData _gameData;
    private readonly HashDatabase? _hashDatabase;
    private readonly ConcurrentDictionary<string, GamePathMatch?> _resolved = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _fileExists = new(StringComparer.Ordinal);
    private readonly Lazy<HashSet<ulong>> _folders;

    public GamePathResolver(GameData gameData, HashDatabase? hashDatabase)
    {
        this._gameData = gameData;
        this._hashDatabase = hashDatabase;
        this._folders = new(this.CollectFolders, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GameData GameData => this._gameData;

    /// <summary>Gets the resolver shared by the users of a file system.</summary>
    public static GamePathResolver Get(SqpackFileSystem fs) =>
        Instances.GetValue(fs, x => new(x.GameData, x.HashDatabase));

    /// <summary>Tests if a file exists.</summary>
    /// <param name="path">Path, without a leading slash; case insensitive.</param>
    public bool FileExists(string path)
    {
        path = path.Trim().TrimStart('/').ToLowerInvariant();
        if (path.Length == 0 || path.EndsWith('/'))
            return false;

        // Lumina throws for paths outside the known categories, which is slow.
        var slash = path.IndexOf('/');
        if (slash <= 0 || !Repository.CategoryNameToIdMap.ContainsKey(path[..slash]))
            return false;

        return this._fileExists.GetOrAdd(
            path,
            static (p, gameData) => {
                try {
                    return gameData.FileExists(p);
                } catch (Exception) {
                    return false;
                }
            },
            this._gameData);
    }

    /// <summary>Gets whether the list of folders used by <see cref="FolderExists"/> has been collected.</summary>
    public bool IsFolderListLoaded => this._folders.IsValueCreated;

    /// <summary>Collects the list of folders used by <see cref="FolderExists"/>, which takes a while; call from a
    /// background thread.</summary>
    public void LoadFolderList() => _ = this._folders.Value;

    /// <summary>Tests if a folder exists, either containing files directly, or as the parent of a known folder.
    /// </summary>
    /// <remarks>The first call collects the list of folders, which takes a while; see <see cref="LoadFolderList"/>.
    /// </remarks>
    /// <param name="path">Path, without a leading slash, with or without a trailing slash; case insensitive.</param>
    public bool FolderExists(string path)
    {
        path = path.Trim().Trim('/').ToLowerInvariant();
        return path.Length != 0 && this._folders.Value.Contains(GetFolderKey(path));
    }

    /// <summary>Makes a text into the form of a game path, if it may be one.</summary>
    /// <returns>Lowercase path without a leading slash, or <c>null</c> if the text cannot be a path.</returns>
    public static string? NormalizeCandidate(string text)
    {
        var t = text.Trim();
        if (t.Length is < 3 or > 260)
            return null;
        if (t.Contains("://", StringComparison.Ordinal))
            return null;
        t = t.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        if (t.Length < 3 || t.Contains("//", StringComparison.Ordinal) || t.StartsWith('.'))
            return null;
        foreach (var c in t) {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' or '.' or '/'))
                return null;
        }

        // A single word is too likely to be something else; paths have folders, and file names have extensions,
        // underscores, or digits.
        if (!t.Contains('/') && !t.Contains('.') && !t.Contains('_') && !t.Any(char.IsAsciiDigit))
            return null;
        return t;
    }

    /// <summary>Finds the existing file or folder that a text refers to.</summary>
    /// <param name="text">Text, such as <c>music/ffxiv/BGM_System_Title.scd</c>, <c>vfx/common/eff/levitate0f</c>,
    /// or <c>chara/monster/m0361/</c>.</param>
    /// <param name="extensionHints">Extensions to prefer for texts without one, such as <c>avfx</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The match, or <c>null</c> if none was found.</returns>
    /// <remarks>Searching for partial paths reads through the known path names, which may take a few milliseconds;
    /// call this from a background thread.</remarks>
    public GamePathMatch? Resolve(
        string text,
        IReadOnlyList<string>? extensionHints = null,
        CancellationToken cancellationToken = default)
    {
        if (NormalizeCandidate(text) is not { } path)
            return null;

        var key = extensionHints is { Count: > 0 } ? $"{path}|{string.Join(',', extensionHints)}" : path;
        if (this._resolved.TryGetValue(key, out var cached))
            return cached;

        var result = this.ResolveCore(path, extensionHints ?? [], cancellationToken);
        this._resolved.TryAdd(key, result);
        return result;
    }

    /// <summary>Gets the result of <see cref="Resolve"/>, if it has been resolved before.</summary>
    public bool TryGetResolved(string text, IReadOnlyList<string>? extensionHints, out GamePathMatch? match)
    {
        match = null;
        if (NormalizeCandidate(text) is not { } path)
            return true;
        var key = extensionHints is { Count: > 0 } ? $"{path}|{string.Join(',', extensionHints)}" : path;
        return this._resolved.TryGetValue(key, out match);
    }

    private GamePathMatch? ResolveCore(string path, IReadOnlyList<string> hints, CancellationToken cancellationToken)
    {
        if (path.EndsWith('/'))
            return this.FolderExists(path) ? new(path, true) : this.FindBySuffix(path, true, hints, cancellationToken);

        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        var hasExtension = dot > 0 && name.Length - dot - 1 is >= 1 and <= 5 &&
            name[(dot + 1)..].All(char.IsAsciiLetterOrDigit);

        if (hasExtension && this.FileExists(path))
            return new(path, false);

        if (!hasExtension) {
            foreach (var ext in hints.Concat(CommonExtensions).Distinct()) {
                if (this.FileExists($"{path}.{ext}"))
                    return new($"{path}.{ext}", false);
            }

            if (path.Contains('/') && this.FolderExists(path))
                return new(path + "/", true);
        }

        return this.FindBySuffix(path, !hasExtension, hints, cancellationToken);
    }

    /// <summary>Finds a known path that ends with the given partial path.</summary>
    private GamePathMatch? FindBySuffix(
        string path,
        bool anyExtension,
        IReadOnlyList<string> hints,
        CancellationToken cancellationToken)
    {
        if (this._hashDatabase is not { } db)
            return null;

        var candidates = db.FindPathsBySuffix(path.TrimEnd('/'), anyExtension, 64, cancellationToken);
        if (candidates.Count == 0)
            return null;

        // Prefer files with hinted extensions, then common ones, then folders, then other files; then shorter paths.
        var best = candidates
            .Select(
                x => {
                    var isFolder = x.EndsWith('/');
                    var ext = isFolder ? string.Empty : x[(x.LastIndexOf('.') + 1)..].ToLowerInvariant();
                    var hintIndex = isFolder ? -1 : IndexOf(hints, ext);
                    var commonIndex = isFolder ? -1 : Array.IndexOf(CommonExtensions, ext);
                    var rank = hintIndex >= 0 ? hintIndex
                        : commonIndex >= 0 ? 100 + commonIndex
                        : isFolder ? 1000
                        : 2000;
                    return (Path: x, IsFolder: isFolder, Rank: rank);
                })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Path.Length)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .First();
        return new(best.Path, best.IsFolder);

        static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (var i = 0; i < list.Count; i++) {
                if (list[i] == value)
                    return i;
            }

            return -1;
        }
    }

    private static ulong GetFolderKey(string lowerPath)
    {
        var slash = lowerPath.IndexOf('/');
        var category = slash < 0 ? lowerPath : lowerPath[..slash];
        var categoryId = Repository.CategoryNameToIdMap.TryGetValue(category, out var id) ? id : (byte) 0xFF;
        return ((ulong) categoryId << 32) | Crc32.Get(Encoding.UTF8.GetBytes(lowerPath));
    }

    /// <summary>Collects the folders that contain files, and the parents of the ones with known names.</summary>
    private HashSet<ulong> CollectFolders()
    {
        var result = new HashSet<ulong>();
        var seen = new HashSet<(uint IndexId, uint Hash)>();
        foreach (var repository in this._gameData.Repositories.Values) {
            foreach (var categories in repository.Categories.Values) {
                foreach (var category in categories) {
                    var indexId = (uint) ((category.CategoryId << 16) | (category.Expansion << 8) | category.Chunk);
                    foreach (var key in category.Index.HashTableEntries.Keys) {
                        var folderHash = unchecked((uint) (key >> 32));
                        if (seen.Add((indexId, folderHash)))
                            result.Add(((ulong) category.CategoryId << 32) | folderHash);
                    }
                }
            }
        }

        if (this._hashDatabase is { } db) {
            var parents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (indexId, hash, name) in db.EnumerateFolders()) {
                if (!seen.Contains((indexId, hash)))
                    continue;

                var lower = name.ToLowerInvariant();
                for (var i = lower.LastIndexOf('/'); i > 0; i = lower.LastIndexOf('/', i - 1)) {
                    var parent = lower[..i];
                    if (!parents.Add(parent))
                        break;
                    result.Add(GetFolderKey(parent));
                }
            }
        }

        return result;
    }
}
