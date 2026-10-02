using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace LuminaExplorer.Core.GameDataNames;

/// <summary>
/// Gives human-readable names to folders under <c>chara/</c>.
/// </summary>
/// <remarks>
/// <para>A name is attached to the shortest folder path that uniquely identifies one model, among all the models
/// known from the game sheets for that model type (named or not).</para>
/// <para>Names for battle NPCs require the BNpcName-BNpcBase link table, which does not exist in the game data.
/// The community-maintained table at <see cref="DefaultBNpcLinkUrl"/> (LuminaSupplemental, GPL-3.0) can be used.</para>
/// </remarks>
public sealed class CharaFolderNames {
    public const string DefaultBNpcLinkUrl =
        "https://raw.githubusercontent.com/Critical-Impact/LuminaSupplemental/main/src/LuminaSupplemental.Excel/Generated/BNpcLink.csv";

    private static readonly string[] DemihumanSlots = { "met", "top", "glv", "dwn", "sho" };

    private readonly Dictionary<string, IReadOnlyList<string>> _entries;
    private readonly (string Path, IReadOnlyList<string> Names, string[] FoldedNames)[] _searchEntries;

    private CharaFolderNames(
        Dictionary<string, IReadOnlyList<string>> entries,
        Dictionary<string, IReadOnlyList<string>> searchEntries)
    {
        this._entries = entries;
        this._searchEntries = searchEntries
            .Select(x => (x.Key, x.Value, x.Value.Select(FoldForSearch).ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Gets all named folders. Keys are normalized folder paths (lowercase, no leading slash, trailing slash),
    /// such as <c>chara/monster/m0361/</c>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Entries => this._entries;

    /// <summary>
    /// Finds folders containing a model with a name that contains all of the given terms.
    /// Unlike <see cref="Entries"/>, this includes folders that contain multiple models, such as equipment sets,
    /// and ignores diacritics (so that "Herklaedi" finds "Herklæði").
    /// </summary>
    /// <param name="terms">Terms that must all appear in the same name.</param>
    /// <returns>Normalized folder paths, and the matching names.</returns>
    public IEnumerable<(string Path, IReadOnlyList<string> MatchingNames)> FindFolders(IReadOnlyList<string> terms)
    {
        var foldedTerms = terms.Where(x => x.Length > 0).Select(FoldForSearch).ToArray();
        if (foldedTerms.Length == 0)
            yield break;

        foreach (var (path, names, foldedNames) in this._searchEntries) {
            var matching = names
                .Where((_, i) => foldedTerms.All(t => foldedNames[i].Contains(t, StringComparison.Ordinal)))
                .ToArray();
            if (matching.Length > 0)
                yield return (path, matching);
        }
    }

    /// <summary>
    /// Folds a text for searching: lowercases it, removes diacritics, and spells out letters such as æ.
    /// </summary>
    public static string FoldForSearch(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD)) {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            switch (char.ToLowerInvariant(c)) {
                case 'æ':
                    sb.Append("ae");
                    break;
                case 'œ':
                    sb.Append("oe");
                    break;
                case 'ø':
                    sb.Append('o');
                    break;
                case 'ð':
                case 'đ':
                    sb.Append('d');
                    break;
                case 'þ':
                    sb.Append("th");
                    break;
                case 'ß':
                    sb.Append("ss");
                    break;
                case var lower:
                    sb.Append(lower);
                    break;
            }
        }

        return sb.ToString();
    }

    public bool TryGetNames(string folderPath, out IReadOnlyList<string> names)
    {
        if (this._entries.TryGetValue(NormalizeFolderPath(folderPath), out var found)) {
            names = found;
            return true;
        }

        names = Array.Empty<string>();
        return false;
    }

    /// <summary>
    /// Normalizes a folder path into the form used as keys of <see cref="Entries"/>.
    /// </summary>
    public static string NormalizeFolderPath(string folderPath)
    {
        var path = folderPath.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
        return path.Length == 0 ? path : path + "/";
    }

    public static string FormatShort(IReadOnlyList<string> names, int max = 3)
    {
        if (names.Count <= max)
            return string.Join(", ", names);
        return string.Join(", ", names.Take(Math.Max(0, max))) + (max > 0 ? ", …" : "…");
    }

    /// <summary>
    /// Downloads a file into <paramref name="cacheFilePath"/>, replacing it only if the download succeeds.
    /// </summary>
    /// <returns><c>true</c> if downloaded; <c>false</c> on any failure, including cancellation.</returns>
    public static async Task<bool> TryDownloadAsync(
        string url,
        string cacheFilePath,
        CancellationToken cancellationToken = default)
    {
        var tempPath = cacheFilePath + ".tmp";
        try {
            if (Path.GetDirectoryName(Path.GetFullPath(cacheFilePath)) is { } dir)
                Directory.CreateDirectory(dir);

            using var client = new HttpClient();
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return false;

            await using (var target = File.Create(tempPath))
                await resp.Content.CopyToAsync(target, cancellationToken);

            File.Move(tempPath, cacheFilePath, true);
            return true;
        } catch (Exception) {
            try {
                File.Delete(tempPath);
            } catch (Exception) {
                // ignore
            }

            return false;
        }
    }

    private static ExcelSheet<T> GetSheet<T>(GameData gameData) where T : struct, IExcelRow<T> =>
        gameData.GetExcelSheet<T>() ?? throw new InvalidDataException($"Sheet {typeof(T).Name} is not available.");

    public static CharaFolderNames Create(GameData gameData, TextReader? bnpcLinkCsv)
    {
        var builder = new Builder(gameData);

        // ModelChara row id -> model, for models that are not human
        var modelCharaModels = new Dictionary<uint, ModelInfo>();
        foreach (var row in GetSheet<ModelChara>(gameData)) {
            if (builder.GetModelCharaModel(row) is { } model)
                modelCharaModels[row.RowId] = model;
        }

        ModelInfo? FromModelChara(uint rowId) => modelCharaModels.GetValueOrDefault(rowId);

        foreach (var row in GetSheet<Mount>(gameData))
            FromModelChara(row.ModelChara.RowId)?.AddDirectName(row.Singular);

        foreach (var row in GetSheet<Companion>(gameData))
            FromModelChara(row.Model.RowId)?.AddDirectName(row.Singular);

        foreach (var row in GetSheet<Ornament>(gameData))
            FromModelChara(row.Model)?.AddDirectName(row.Singular);

        foreach (var row in GetSheet<Item>(gameData)) {
            if (row.EquipSlotCategory.RowId == 0 || !row.EquipSlotCategory.IsValid)
                continue;

            var esc = row.EquipSlotCategory.Value;
            if (esc.MainHand > 0 || esc.OffHand > 0) {
                builder.GetWeaponModel(row.ModelMain)?.AddDirectName(row.Singular);
                builder.GetWeaponModel(row.ModelSub)?.AddDirectName(row.Singular);
                continue;
            }

            if (GetGearSlot(esc) is ({ } kind, { } slot))
                builder.GetGearModel(kind, slot, row.ModelMain)?.AddDirectName(row.Singular);
        }

        var enpcResidentSheet = GetSheet<ENpcResident>(gameData);
        foreach (var row in GetSheet<ENpcBase>(gameData)) {
            if (FromModelChara(row.ModelChara.RowId) is { } model &&
                enpcResidentSheet.GetRowOrDefault(row.RowId) is { } resident)
                model.AddDirectName(resident.Singular);
        }

        if (bnpcLinkCsv is not null) {
            var bnpcBaseSheet = GetSheet<BNpcBase>(gameData);
            var bnpcNameSheet = GetSheet<BNpcName>(gameData);
            while (bnpcLinkCsv.ReadLine() is { } line) {
                var parts = line.Split(',');
                if (parts.Length < 2 ||
                    !uint.TryParse(parts[0].Trim(), out var nameId) ||
                    !uint.TryParse(parts[1].Trim(), out var baseId))
                    continue;

                if (bnpcBaseSheet.GetRowOrDefault(baseId) is not { } bnpcBase ||
                    FromModelChara(bnpcBase.ModelChara.RowId) is not { } model ||
                    bnpcNameSheet.GetRowOrDefault(nameId) is not { } bnpcName)
                    continue;

                model.AddBNpcName(bnpcName.Singular, nameId);
            }
        }

        var (entries, searchEntries) = builder.Build();
        return new(entries, searchEntries);
    }

    internal static (string Kind, string Slot)? GetGearSlot(EquipSlotCategory esc)
    {
        if (esc.Head > 0)
            return ("equipment", "met");
        if (esc.Body > 0)
            return ("equipment", "top");
        if (esc.Gloves > 0)
            return ("equipment", "glv");
        if (esc.Legs > 0)
            return ("equipment", "dwn");
        if (esc.Feet > 0)
            return ("equipment", "sho");
        if (esc.Ears > 0)
            return ("accessory", "ear");
        if (esc.Neck > 0)
            return ("accessory", "nek");
        if (esc.Wrists > 0)
            return ("accessory", "wrs");
        if (esc.FingerL > 0 || esc.FingerR > 0)
            return ("accessory", "rir");
        return null;
    }

    private sealed class Builder {
        private readonly GameData _gameData;
        private readonly Dictionary<string, ModelInfo> _models = new();
        private readonly Dictionary<string, bool> _existence = new();

        public Builder(GameData gameData)
        {
            this._gameData = gameData;
        }

        public ModelInfo? GetModelCharaModel(ModelChara row) => row.Type switch {
            2 => this.GetIdBodyModel("demihuman", 'd', row.Model, "equipment", 'e', row.Base),
            3 => this.GetIdBodyModel("monster", 'm', row.Model, "body", 'b', row.Base),
            4 => this.GetIdBodyModel("weapon", 'w', row.Model, "body", 'b', row.Base),
            _ => null,
        };

        public ModelInfo? GetWeaponModel(ulong model) => this.GetIdBodyModel(
            "weapon",
            'w',
            (int) (model & 0xFFFF),
            "body",
            'b',
            (int) ((model >> 16) & 0xFFFF));

        public ModelInfo? GetGearModel(string kind, string slot, ulong model)
        {
            var setId = (int) (model & 0xFFFF);
            if (setId == 0)
                return null;

            var folder = $"chara/{kind}/{kind[0]}{setId:D4}/";
            var identity = $"{folder}|{slot}";
            if (!this._models.TryGetValue(identity, out var info))
                this._models.Add(identity, info = new(new[] { folder }));
            return info;
        }

        private ModelInfo? GetIdBodyModel(string kind, char idPrefix, int id, string subKind, char subPrefix, int subId)
        {
            if (id == 0)
                return null;

            var root = $"chara/{kind}/{idPrefix}{id:D4}/";
            var leaf = $"{root}obj/{subKind}/{subPrefix}{subId:D4}/";
            if (this._models.TryGetValue(leaf, out var info))
                return info;
            if (!this.FolderExists(kind, leaf, $"{idPrefix}{id:D4}{subPrefix}{subId:D4}", $"{subPrefix}{subId:D4}"))
                return null;

            this._models.Add(leaf, info = new(new[] { root, $"{root}obj/", $"{root}obj/{subKind}/", leaf }));
            return info;
        }

        private bool FolderExists(string kind, string leaf, string modelStem, string imcStem)
        {
            if (this._existence.TryGetValue(leaf, out var exists))
                return exists;

            exists = this._gameData.FileExists($"{leaf}{imcStem}.imc");
            if (!exists) {
                exists = kind == "demihuman"
                    ? DemihumanSlots.Any(x => this._gameData.FileExists($"{leaf}model/{modelStem}_{x}.mdl"))
                    : this._gameData.FileExists($"{leaf}model/{modelStem}.mdl");
            }

            this._existence.Add(leaf, exists);
            return exists;
        }

        public (Dictionary<string, IReadOnlyList<string>> Entries, Dictionary<string, IReadOnlyList<string>> SearchEntries)
            Build()
        {
            var prefixCounts = new Dictionary<string, int>();
            foreach (var model in this._models.Values) {
                foreach (var prefix in model.Prefixes)
                    prefixCounts[prefix] = prefixCounts.GetValueOrDefault(prefix) + 1;
            }

            var result = new Dictionary<string, IReadOnlyList<string>>();
            var search = new Dictionary<string, List<string>>();
            foreach (var model in this._models.Values) {
                var names = model.GetNames();
                if (names.Count == 0)
                    continue;

                if (model.Prefixes.FirstOrDefault(x => prefixCounts[x] == 1) is { } prefix) {
                    result.Add(prefix, names);
                    search[prefix] = [..names];
                } else {
                    // Shared by multiple models (e.g. an equipment set); searchable by the names of all of them.
                    var folder = model.Prefixes[^1];
                    if (!search.TryGetValue(folder, out var list))
                        search.Add(folder, list = []);
                    foreach (var name in names) {
                        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                            list.Add(name);
                    }
                }
            }

            return (result, search.ToDictionary(x => x.Key, x => (IReadOnlyList<string>) x.Value.ToArray()));
        }
    }

    private sealed class ModelInfo {
        private readonly List<string> _directNames = new();
        private readonly Dictionary<string, (int Count, uint MinId)> _bnpcNames = new(StringComparer.OrdinalIgnoreCase);

        public ModelInfo(string[] prefixes)
        {
            this.Prefixes = prefixes;
        }

        /// <summary>
        /// Gets the candidate folder paths, from the shortest.
        /// </summary>
        public string[] Prefixes { get; }

        public void AddDirectName(ReadOnlySeString name)
        {
            var text = name.ExtractText().Trim();
            if (text.Length != 0)
                this._directNames.Add(text);
        }

        public void AddBNpcName(ReadOnlySeString name, uint nameId)
        {
            var text = name.ExtractText().Trim();
            if (text.Length == 0)
                return;

            this._bnpcNames[text] = this._bnpcNames.TryGetValue(text, out var prev)
                ? (prev.Count + 1, Math.Min(prev.MinId, nameId))
                : (1, nameId);
        }

        public IReadOnlyList<string> GetNames()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var name in this._directNames.Concat(
                         this._bnpcNames
                             .OrderByDescending(x => x.Value.Count)
                             .ThenBy(x => x.Value.MinId)
                             .Select(x => x.Key))) {
                if (seen.Add(name))
                    result.Add(name);
            }

            return result.ToArray();
        }
    }
}
