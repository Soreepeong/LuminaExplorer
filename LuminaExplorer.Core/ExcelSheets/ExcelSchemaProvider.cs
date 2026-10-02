using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.ExcelSheets;

/// <summary>Provides sheet definitions from <a href="https://github.com/xivdev/EXDSchema">EXDSchema</a>.</summary>
/// <remarks>
/// <para>The schema repository has a branch <c>ver/&lt;game version&gt;</c> for each game version that changed sheet
/// layouts. The greatest of those that is not newer than the installed game is used, falling back to <c>latest</c>.
/// </para>
/// <para>The branch is downloaded once as a zip archive into the cache directory, and an index file remembers which
/// branch was chosen for which game version, so that the GitHub API is only queried when the game gets updated.</para>
/// </remarks>
public sealed class ExcelSchemaProvider {
    public const string DefaultRepository = "xivdev/EXDSchema";
    public const string FallbackBranch = "latest";
    private const string IndexFileName = "index.json";
    private const string VersionBranchPrefix = "ver/";

    private static readonly Lazy<HttpClient> LazyHttpClient = new(
        () => {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.UserAgent.Add(new("LuminaExplorer", "1.0"));
            return client;
        });

    private readonly object _loadLock = new();
    private Task<ExcelSchemaLoadResult>? _loadTask;

    /// <param name="cacheDirectory">Directory to store downloaded schemas in.</param>
    /// <param name="gameDirectory">The <c>game</c> directory of the installation, containing
    /// <c>ffxivgame.ver</c> and <c>sqpack</c>.</param>
    /// <param name="repository">GitHub repository, in the form of <c>owner/name</c>.</param>
    public ExcelSchemaProvider(string cacheDirectory, string? gameDirectory, string? repository = null)
    {
        this.CacheDirectory = cacheDirectory;
        this.GameDirectory = gameDirectory;
        this.Repository = string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository.Trim().Trim('/');
    }

    public string CacheDirectory { get; }

    public string? GameDirectory { get; }

    public string Repository { get; }

    /// <summary>Reads the version of the game from <c>ffxivgame.ver</c>.</summary>
    /// <returns>The version, such as <c>2026.09.15.0000.0000</c>, or <c>null</c> if unavailable.</returns>
    public static string? ReadGameVersion(string? gameDirectory)
    {
        if (string.IsNullOrEmpty(gameDirectory))
            return null;
        try {
            var version = File.ReadAllText(Path.Combine(gameDirectory, "ffxivgame.ver")).Trim();
            return TryParseVersion(version, out _) ? version : null;
        } catch (Exception) {
            return null;
        }
    }

    /// <summary>Loads the schema set, downloading it if necessary. The result is cached; this never throws.</summary>
    public Task<ExcelSchemaLoadResult> GetSchemaSetAsync()
    {
        lock (this._loadLock) {
            return this._loadTask ??= Task.Run(this.LoadAsync);
        }
    }

    private async Task<ExcelSchemaLoadResult> LoadAsync()
    {
        var gameVersion = ReadGameVersion(this.GameDirectory);
        var versionKey = gameVersion ?? "unknown";
        string? branch;
        var notes = new List<string>();

        try {
            Directory.CreateDirectory(this.CacheDirectory);
        } catch (Exception e) {
            return new(null, $"Cannot create the schema cache directory: {e.Message}");
        }

        var index = this.ReadIndex();

        // 1. Previously chosen branch for this game version.
        if (index.TryGetValue(versionKey, out branch) && File.Exists(this.GetZipPath(branch))) {
            if (TryOpen(branch, out var set, out var error))
                return new(set, null);
            notes.Add(error);
        }

        // 2. Ask GitHub which branches exist, and pick the best one.
        var (branches, listError) = await this.TryListBranchesAsync();
        if (branches is not null) {
            branch = ChooseBranch(branches, gameVersion);
            if (branch is null)
                return new(null, $"No usable schema branch in {this.Repository}.");

            if (File.Exists(this.GetZipPath(branch)) || await this.TryDownloadAsync(branch, notes)) {
                if (TryOpen(branch, out var set, out var error)) {
                    index[versionKey] = branch;
                    this.WriteIndex(index);
                    return new(set, null);
                }

                notes.Add(error);
            }
        } else {
            notes.Add(listError ?? "Failed to list schema branches.");
        }

        // 3. Offline, rate limited, or the download failed: use the best previously downloaded one, if any.
        var cached = index.Values.Distinct().Where(x => File.Exists(this.GetZipPath(x))).ToArray();
        if (ChooseBranch(cached, gameVersion) is { } cachedBranch && TryOpen(cachedBranch, out var cachedSet, out _))
            return new(cachedSet, string.Join(" ", notes.Prepend("Using a previously downloaded schema.")));

        return new(null, string.Join(" ", notes));

        bool TryOpen(string b, out ExcelSchemaSet? set, out string error)
        {
            try {
                set = new(b, gameVersion, this.GetZipPath(b));
                error = string.Empty;
                return true;
            } catch (Exception e) {
                set = null;
                error = $"Failed to open the cached schema {b}: {e.Message}";
                try {
                    File.Delete(this.GetZipPath(b));
                } catch (Exception) {
                    // ignore
                }

                return false;
            }
        }
    }

    /// <summary>Chooses the greatest <c>ver/X</c> branch not newer than the game, or <c>latest</c>.</summary>
    public static string? ChooseBranch(IEnumerable<string> branches, string? gameVersion)
    {
        var list = branches.ToArray();
        if (gameVersion is not null && TryParseVersion(gameVersion, out var current)) {
            var best = list
                .Select(x => (Branch: x, Ok: TryParseBranchVersion(x, out var v), Version: v))
                .Where(x => x.Ok && CompareVersion(x.Version, current) <= 0)
                .OrderByDescending(x => x.Version, Comparer<long[]>.Create(CompareVersion))
                .Select(x => x.Branch)
                .FirstOrDefault();
            if (best is not null)
                return best;
        }

        if (list.Contains(FallbackBranch))
            return FallbackBranch;

        // Unknown game version and no "latest": the newest versioned branch is the best guess.
        return list
            .Select(x => (Branch: x, Ok: TryParseBranchVersion(x, out var v), Version: v))
            .Where(x => x.Ok)
            .OrderByDescending(x => x.Version, Comparer<long[]>.Create(CompareVersion))
            .Select(x => x.Branch)
            .FirstOrDefault();
    }

    private static bool TryParseBranchVersion(string branch, out long[] version)
    {
        version = [];
        return branch.StartsWith(VersionBranchPrefix, StringComparison.Ordinal) &&
            TryParseVersion(branch[VersionBranchPrefix.Length..], out version);
    }

    private static bool TryParseVersion(string s, out long[] version)
    {
        var parts = s.Trim().Split('.');
        version = new long[parts.Length];
        if (parts.Length < 2)
            return false;
        for (var i = 0; i < parts.Length; i++) {
            if (!long.TryParse(parts[i], out version[i]))
                return false;
        }

        return true;
    }

    private static int CompareVersion(long[]? a, long[]? b)
    {
        a ??= [];
        b ??= [];
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++) {
            var c = (i < a.Length ? a[i] : 0).CompareTo(i < b.Length ? b[i] : 0);
            if (c != 0)
                return c;
        }

        return 0;
    }

    private string GetZipPath(string branch) =>
        Path.Combine(this.CacheDirectory, Regex.Replace(branch, @"[^A-Za-z0-9._-]", "_") + ".zip");

    private Dictionary<string, string> ReadIndex()
    {
        try {
            var path = Path.Combine(this.CacheDirectory, IndexFileName);
            if (File.Exists(path)) {
                var index = JsonSerializer.Deserialize<IndexDto>(File.ReadAllText(path, Encoding.UTF8));
                if (index?.Repository == this.Repository && index.Branches is not null)
                    return new(index.Branches);
            }
        } catch (Exception) {
            // ignore; start over
        }

        return new();
    }

    private void WriteIndex(Dictionary<string, string> branches)
    {
        try {
            var path = Path.Combine(this.CacheDirectory, IndexFileName);
            var json = JsonSerializer.Serialize(
                new IndexDto { Repository = this.Repository, Branches = branches },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path + ".tmp", json, new UTF8Encoding(false));
            File.Move(path + ".tmp", path, true);
        } catch (Exception) {
            // ignore; the branch list will be queried again next time
        }
    }

    private async Task<(List<string>? Branches, string? Error)> TryListBranchesAsync()
    {
        var result = new List<string>();
        try {
            for (var page = 1; page <= 50; page++) {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.github.com/repos/{this.Repository}/branches?per_page=100&page={page}");
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                using var resp = await LazyHttpClient.Value.SendAsync(req);
                if (!resp.IsSuccessStatusCode) {
                    var rateLimited = resp.StatusCode is HttpStatusCode.TooManyRequests ||
                        (resp.StatusCode is HttpStatusCode.Forbidden &&
                            resp.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                            remaining.FirstOrDefault() == "0");
                    return (null, rateLimited
                        ? "GitHub API rate limit exceeded; try again later."
                        : $"Failed to list schema branches: HTTP {(int) resp.StatusCode} {resp.ReasonPhrase}.");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                var count = 0;
                foreach (var item in doc.RootElement.EnumerateArray()) {
                    count++;
                    if (item.TryGetProperty("name", out var name) && name.GetString() is { } n)
                        result.Add(n);
                }

                var hasNext = resp.Headers.TryGetValues("Link", out var links) &&
                    links.Any(x => x.Contains("rel=\"next\"", StringComparison.Ordinal));
                if (!hasNext || count == 0)
                    break;
            }

            return (result, null);
        } catch (Exception e) {
            return (null, $"Failed to list schema branches: {e.Message}");
        }
    }

    private async Task<bool> TryDownloadAsync(string branch, List<string> notes)
    {
        var path = this.GetZipPath(branch);
        var tempPath = path + ".tmp";
        try {
            var url = $"https://github.com/{this.Repository}/archive/refs/heads/{branch}.zip";
            using var resp = await LazyHttpClient.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) {
                notes.Add($"Failed to download schema {branch}: HTTP {(int) resp.StatusCode} {resp.ReasonPhrase}.");
                return false;
            }

            await using (var target = File.Create(tempPath))
                await resp.Content.CopyToAsync(target);

            // Make sure that it is a valid archive before replacing anything.
            using (ZipFile.OpenRead(tempPath)) { }

            File.Move(tempPath, path, true);
            return true;
        } catch (Exception e) {
            notes.Add($"Failed to download schema {branch}: {e.Message}");
            try {
                File.Delete(tempPath);
            } catch (Exception) {
                // ignore
            }

            return false;
        }
    }

    private sealed class IndexDto {
        // ReSharper disable UnusedAutoPropertyAccessor.Local
        public string? Repository { get; set; }
        public Dictionary<string, string>? Branches { get; set; }
        // ReSharper restore UnusedAutoPropertyAccessor.Local
    }
}

/// <summary>Result of <see cref="ExcelSchemaProvider.GetSchemaSetAsync"/>.</summary>
/// <param name="Set">The schema set, or <c>null</c> if unavailable.</param>
/// <param name="Message">Description of the problem, if any.</param>
public sealed record ExcelSchemaLoadResult(ExcelSchemaSet? Set, string? Message);

/// <summary>Sheet definitions from a downloaded EXDSchema branch archive. Thread safe.</summary>
public sealed class ExcelSchemaSet : IDisposable {
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly ConcurrentDictionary<string, (ExcelSchemaSheet? Sheet, string? Error)> _sheets =
        new(StringComparer.OrdinalIgnoreCase);

    public ExcelSchemaSet(string branch, string? gameVersion, string zipPath)
    {
        this.Branch = branch;
        this.GameVersion = gameVersion;
        this._archive = ZipFile.OpenRead(zipPath);
        try {
            // The archive contains a single root directory, such as "EXDSchema-ver-2026.09.01.0000.0000/".
            this._entries = new(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in this._archive.Entries) {
                var parts = entry.FullName.Split('/');
                if (parts.Length == 2 && parts[1].EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    this._entries[parts[1][..^4]] = entry;
            }

            if (this._entries.Count == 0)
                throw new InvalidDataException("The archive does not contain any sheet definitions.");
        } catch (Exception) {
            this._archive.Dispose();
            throw;
        }
    }

    /// <summary>Gets the name of the branch, such as <c>ver/2026.09.01.0000.0000</c>.</summary>
    public string Branch { get; }

    /// <summary>Gets the version of the installed game, if known.</summary>
    public string? GameVersion { get; }

    public IEnumerable<string> SheetNames => this._entries.Keys;

    public bool Contains(string sheetName) => this._entries.ContainsKey(sheetName);

    /// <summary>Gets the parsed definition of a sheet.</summary>
    /// <param name="sheetName">Name of the sheet, as in <c>root.exl</c>.</param>
    /// <param name="error">Parse error, if any.</param>
    /// <returns>The definition, or <c>null</c> if not available.</returns>
    public ExcelSchemaSheet? TryGetSheet(string sheetName, out string? error)
    {
        var r = this._sheets.GetOrAdd(sheetName, this.Load);
        error = r.Error;
        return r.Sheet;
    }

    private (ExcelSchemaSheet?, string?) Load(string sheetName)
    {
        if (!this._entries.TryGetValue(sheetName, out var entry))
            return (null, null);

        try {
            string text;
            lock (this._archive) {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                text = reader.ReadToEnd();
            }

            return (ExcelSchemaSheet.Parse(new StringReader(text), sheetName), null);
        } catch (Exception e) {
            return (null, e.Message);
        }
    }

    public void Dispose()
    {
        lock (this._archive)
            this._archive.Dispose();
    }
}
