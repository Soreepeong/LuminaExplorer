using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Lumina.Data;

namespace LuminaExplorer.Core.ExcelSheets.Index;

/// <summary>A cell of an Excel sheet that contains an indexed value.</summary>
/// <param name="SheetIndex">Index into <see cref="ExcelValueIndex.SheetNames"/>.</param>
/// <param name="RowId">Row ID.</param>
/// <param name="SubrowId">Subrow ID; 0 for sheets without subrows.</param>
/// <param name="ColumnIndex">Index of the column in the header.</param>
/// <param name="DerivedFrom">For a game path derived from a number in the cell (such as an icon ID), the number as
/// text; <c>null</c> if the value is the text of the cell.</param>
public readonly record struct ExcelValueCell(
    int SheetIndex,
    uint RowId,
    ushort SubrowId,
    int ColumnIndex,
    string? DerivedFrom = null);

/// <summary>A cell that matched a search.</summary>
/// <param name="SheetName">Name of the sheet, such as <c>Item</c> or <c>quest/000/ClsArc001_00001</c>.</param>
/// <param name="RowId">Row ID.</param>
/// <param name="SubrowId">Subrow ID; 0 for sheets without subrows.</param>
/// <param name="ColumnIndex">Index of the column in the header.</param>
/// <param name="Value">Text of the cell, with line breaks replaced by spaces; or the game path derived from the
/// cell, if <paramref name="DerivedFrom"/> is set.</param>
/// <param name="SheetHasSubrows">Whether the sheet has subrows.</param>
/// <param name="DerivedFrom">For a game path derived from a number in the cell (such as an icon ID), the number as
/// text, such as <c>26003</c> or <c>e0100 v1 (top)</c>; <c>null</c> if <paramref name="Value"/> is the text of the
/// cell.</param>
public sealed record ExcelValueHit(
    string SheetName,
    uint RowId,
    ushort SubrowId,
    int ColumnIndex,
    string Value,
    bool SheetHasSubrows,
    string? DerivedFrom = null) {
    public bool IsDerived => this.DerivedFrom is not null;
}

/// <summary>Result of <see cref="ExcelValueIndex.Search"/>.</summary>
/// <param name="Hits">Matching cells, best matches first; at most the requested number.</param>
/// <param name="TotalCells">Number of matching cells, including the ones not in <paramref name="Hits"/>.</param>
/// <param name="TotalIsLowerBound">Whether the search stopped early, so that there may be more matching cells.</param>
public sealed record ExcelValueSearchResult(IReadOnlyList<ExcelValueHit> Hits, int TotalCells, bool TotalIsLowerBound);

/// <summary>Progress of <see cref="ExcelValueIndexBuilder"/>.</summary>
public readonly record struct ExcelValueIndexProgress(int SheetsDone, int SheetCount) {
    public double Fraction => this.SheetCount == 0 ? 0 : (double) this.SheetsDone / this.SheetCount;
}

/// <summary>An index of the text in the string cells of all Excel sheets, for substring searches.</summary>
/// <remarks>
/// <para>Distinct values are stored once in a NUL-separated, lowercased UTF-8 blob that is searched with a vectorized
/// substring search; each value points to the list of cells that contain it.</para>
/// <para>Numeric cells that refer to game paths (icons, models; see <see cref="ExcelDerivedReferences"/>) are indexed
/// by the derived path, as cells marked as derived, along with the number they were derived from.</para>
/// </remarks>
public sealed class ExcelValueIndex {
    /// <summary>Changes whenever the file format or the way the values are extracted changes.</summary>
    public const int FormatVersion = 3;

    private const uint Magic = 0x4956584C; // "LXVI"

    // Values: [_lowerOffsets[i], _lowerOffsets[i + 1] - 1) in _lower; the byte before the next value is a NUL.
    private readonly byte[] _lower;
    private readonly int[] _lowerOffsets;
    private readonly byte[] _text;
    private readonly int[] _textOffsets;

    // Cells of value i: [_cellOffsets[i], _cellOffsets[i + 1]).
    private readonly int[] _cellOffsets;
    private readonly ushort[] _cellSheets;
    private readonly bool[] _sheetHasSubrows;
    private readonly uint[] _cellRows;
    private readonly ushort[] _cellSubrows;
    private readonly ushort[] _cellColumns;

    // Cells holding derived values: sorted indices into the cell arrays, and indices into _sourceTexts.
    private readonly int[] _derivedCells;
    private readonly int[] _derivedSources;
    private readonly string[] _sourceTexts;

    internal ExcelValueIndex(
        string versionKey,
        Language language,
        string[] sheetNames,
        bool[] sheetHasSubrows,
        byte[] lower,
        int[] lowerOffsets,
        byte[] text,
        int[] textOffsets,
        int[] cellOffsets,
        ushort[] cellSheets,
        uint[] cellRows,
        ushort[] cellSubrows,
        ushort[] cellColumns,
        int[] derivedCells,
        int[] derivedSources,
        string[] sourceTexts)
    {
        this.VersionKey = versionKey;
        this.Language = language;
        this.SheetNames = sheetNames;
        this._sheetHasSubrows = sheetHasSubrows;
        this._lower = lower;
        this._lowerOffsets = lowerOffsets;
        this._text = text;
        this._textOffsets = textOffsets;
        this._cellOffsets = cellOffsets;
        this._cellSheets = cellSheets;
        this._cellRows = cellRows;
        this._cellSubrows = cellSubrows;
        this._cellColumns = cellColumns;
        this._derivedCells = derivedCells;
        this._derivedSources = derivedSources;
        this._sourceTexts = sourceTexts;
    }

    /// <summary>Gets the key of the game data that this index was built from; see <see cref="GetGameVersionKey"/>.
    /// </summary>
    public string VersionKey { get; }

    /// <summary>Gets the preferred language that this index was built for.</summary>
    public Language Language { get; }

    public IReadOnlyList<string> SheetNames { get; }

    /// <summary>Gets whether the sheet at the given index of <see cref="SheetNames"/> has subrows.</summary>
    public bool SheetHasSubrows(int sheetIndex) => this._sheetHasSubrows[sheetIndex];

    public int ValueCount => this._lowerOffsets.Length - 1;

    public int CellCount => this._cellRows.Length;

    /// <summary>Gets the number of cells holding game paths derived from numbers.</summary>
    public int DerivedCellCount => this._derivedCells.Length;

    /// <summary>Gets the approximate number of bytes used in memory.</summary>
    public long MemorySize =>
        this._lower.LongLength + this._text.LongLength +
        4L * (this._lowerOffsets.Length + this._textOffsets.Length + this._cellOffsets.Length) +
        10L * this._cellRows.Length +
        8L * this._derivedCells.Length +
        this._sourceTexts.Sum(x => 24L + 2L * x.Length);

    /// <summary>Gets the text of a value.</summary>
    public string GetValue(int valueIndex) =>
        Encoding.UTF8.GetString(
            this._text,
            this._textOffsets[valueIndex],
            this._textOffsets[valueIndex + 1] - this._textOffsets[valueIndex] - 1);

    /// <summary>Gets the cells that contain a value.</summary>
    public IEnumerable<ExcelValueCell> GetCells(int valueIndex)
    {
        for (var i = this._cellOffsets[valueIndex]; i < this._cellOffsets[valueIndex + 1]; i++)
            yield return this.GetCell(i);
    }

    private ExcelValueCell GetCell(int i) =>
        new(this._cellSheets[i], this._cellRows[i], this._cellSubrows[i], this._cellColumns[i], this.GetDerivedFrom(i));

    /// <summary>Gets the text that the value of a cell was derived from, if it is derived.</summary>
    private string? GetDerivedFrom(int cellIndex)
    {
        var i = Array.BinarySearch(this._derivedCells, cellIndex);
        return i < 0 ? null : this._sourceTexts[this._derivedSources[i]];
    }

    private ExcelValueHit MakeHit(int cellIndex, string text) =>
        new(
            this.SheetNames[this._cellSheets[cellIndex]],
            this._cellRows[cellIndex],
            this._cellSubrows[cellIndex],
            this._cellColumns[cellIndex],
            text,
            this._sheetHasSubrows[this._cellSheets[cellIndex]],
            this.GetDerivedFrom(cellIndex));

    /// <summary>Finds the cells whose values contain all of the given terms, case-insensitively.</summary>
    /// <param name="terms">Terms. A term that looks like a file name with an extension (<c>name.scd</c>) also
    /// matches values that contain the name without the extension, unless followed by more of a name.</param>
    /// <param name="maxHits">Maximum number of cells to return.</param>
    /// <param name="maxValues">Maximum number of distinct values to look at before giving up on finding more.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ExcelValueSearchResult Search(
        IReadOnlyList<string> terms,
        int maxHits,
        int maxValues = 20000,
        CancellationToken cancellationToken = default)
    {
        var searchTerms = terms
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new SearchTerm(x))
            .OrderByDescending(x => x.Required.Length)
            .ToArray();
        if (searchTerms.Length == 0 || this.ValueCount == 0)
            return new([], 0, false);

        var haystack = this._lower.AsSpan();
        var needle = searchTerms[0].Required;
        var matchedValues = new List<int>();
        var stoppedEarly = false;

        var position = 0;
        while (position < haystack.Length) {
            cancellationToken.ThrowIfCancellationRequested();

            var found = haystack[position..].IndexOf(needle);
            if (found < 0)
                break;
            found += position;

            var valueIndex = this.FindValueAt(found);
            var start = this._lowerOffsets[valueIndex];
            var end = this._lowerOffsets[valueIndex + 1] - 1;
            position = end + 1;

            var value = haystack[start..end];
            var allMatch = true;
            foreach (var t in searchTerms) {
                if (!t.IsMatch(value)) {
                    allMatch = false;
                    break;
                }
            }

            if (!allMatch)
                continue;

            if (matchedValues.Count >= maxValues) {
                stoppedEarly = true;
                break;
            }

            matchedValues.Add(valueIndex);
        }

        var totalCells = 0;
        foreach (var v in matchedValues)
            totalCells += this._cellOffsets[v + 1] - this._cellOffsets[v];

        var ranked = matchedValues
            .Select(v => (Value: v, Rank: this.Rank(v, searchTerms)))
            .OrderBy(x => x.Rank.Kind)
            .ThenBy(x => x.Rank.Length)
            .ThenBy(x => x.Value);

        var hits = new List<ExcelValueHit>();
        foreach (var (v, _) in ranked) {
            if (hits.Count >= maxHits)
                break;
            var text = this.GetValue(v);
            for (var i = this._cellOffsets[v]; i < this._cellOffsets[v + 1] && hits.Count < maxHits; i++)
                hits.Add(this.MakeHit(i, text));
        }

        return new(hits, totalCells, stoppedEarly);
    }

    /// <summary>Finds the cells that refer to a game file or folder.</summary>
    /// <param name="path">Full path of the file or folder, such as <c>music/ffxiv/BGM_System_Title.scd</c> or
    /// <c>chara/monster/m0361/obj/body/b0001/</c>.</param>
    /// <param name="isFolder">Whether <paramref name="path"/> is a folder.</param>
    /// <param name="maxHits">Maximum number of cells to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cells, best matches first.</returns>
    /// <remarks>
    /// <para>For a file, values that are the path, the path without the extension, or the end of either at a folder
    /// boundary (such as <c>ability/2ax_warrior/abl001</c> for <c>chara/action/ability/2ax_warrior/abl001.tmb</c>)
    /// match; so do the file name, and the file name without the extension if it does not look like a plain word.
    /// </para>
    /// <para>For a folder, values that are the folder, a path in it (including derived model paths), or the end of
    /// the folder path at a folder boundary (such as <c>a2d1/01</c> for <c>ui/map/a2d1/01/</c>) match.</para>
    /// </remarks>
    public ExcelValueSearchResult FindReferences(
        string path,
        bool isFolder,
        int maxHits,
        CancellationToken cancellationToken = default)
    {
        var p = path.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
        if (p.Length == 0 || this.ValueCount == 0)
            return new([], 0, false);

        var target = new ReferenceTarget(p, isFolder);
        var haystack = this._lower.AsSpan();
        var needle = target.Needle;
        var matched = new List<(int Value, int Kind)>();

        for (var position = 0; position < haystack.Length;) {
            cancellationToken.ThrowIfCancellationRequested();

            var found = haystack[position..].IndexOf(needle);
            if (found < 0)
                break;
            found += position;

            var valueIndex = this.FindValueAt(found);
            var start = this._lowerOffsets[valueIndex];
            var end = this._lowerOffsets[valueIndex + 1] - 1;
            position = end + 1;

            var kind = target.Classify(haystack[start..end]);
            if (kind >= 0)
                matched.Add((valueIndex, kind));
        }

        var totalCells = 0;
        foreach (var (v, _) in matched)
            totalCells += this._cellOffsets[v + 1] - this._cellOffsets[v];

        var hits = new List<ExcelValueHit>();
        foreach (var (v, _) in matched.OrderBy(x => x.Kind).ThenBy(x => x.Value)) {
            if (hits.Count >= maxHits)
                break;
            var text = this.GetValue(v);
            for (var i = this._cellOffsets[v]; i < this._cellOffsets[v + 1] && hits.Count < maxHits; i++)
                hits.Add(this.MakeHit(i, text));
        }

        return new(hits, totalCells, false);
    }

    /// <summary>Finds the index of the value containing the given offset of the lowercased blob.</summary>
    private int FindValueAt(int offset)
    {
        var i = Array.BinarySearch(this._lowerOffsets, offset);
        return i >= 0 ? i : ~i - 1;
    }

    /// <summary>
    /// Ranks a match: values (or their last path component, or its stem) equal to a term first, then values whose
    /// last path component starts with a term, then the rest; shorter values first within each.
    /// </summary>
    private (int Kind, int Length) Rank(int valueIndex, SearchTerm[] terms)
    {
        var start = this._lowerOffsets[valueIndex];
        var value = this._lower.AsSpan(start, this._lowerOffsets[valueIndex + 1] - 1 - start);
        var name = value[(value.LastIndexOf((byte) '/') + 1)..];
        var dot = name.LastIndexOf((byte) '.');
        var stem = dot > 0 ? name[..dot] : name;

        var kind = 2;
        foreach (var t in terms) {
            if (value.SequenceEqual(t.Full) ||
                (t.NameStem.Length > 0 &&
                    (name.SequenceEqual(t.Name) || stem.SequenceEqual(t.NameStem) || name.SequenceEqual(t.NameStem)))) {
                kind = 0;
                break;
            }

            if (t.NameStem.Length > 0 && name.StartsWith(t.NameStem))
                kind = 1;

            // A folder of a path, such as m0361 in chara/monster/m0361/obj/body/b0001/.
            if (t.NameStem.Length > 0 && name.Length != value.Length && HasComponent(value, t.NameStem)) {
                kind = 0;
                break;
            }
        }

        return (kind, value.Length);

        static bool HasComponent(ReadOnlySpan<byte> path, ReadOnlySpan<byte> component)
        {
            foreach (var range in path.Split((byte) '/')) {
                if (path[range].SequenceEqual(component))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Gets a string identifying the version of the game data in an installation.</summary>
    /// <param name="sqpackDirectory">The <c>game/sqpack</c> directory.</param>
    public static string GetGameVersionKey(DirectoryInfo sqpackDirectory)
    {
        var sb = new StringBuilder();
        TryAppend(Path.Combine(sqpackDirectory.Parent?.FullName ?? sqpackDirectory.FullName, "ffxivgame.ver"));
        if (sqpackDirectory.Exists) {
            foreach (var d in sqpackDirectory.EnumerateDirectories("ex*").OrderBy(x => x.Name, StringComparer.Ordinal))
                TryAppend(Path.Combine(d.FullName, d.Name + ".ver"));
        }

        return sb.ToString();

        void TryAppend(string path)
        {
            try {
                sb.Append(File.ReadAllText(path).Trim()).Append(';');
            } catch (IOException) {
                sb.Append("?;");
            } catch (UnauthorizedAccessException) {
                sb.Append("?;");
            }
        }
    }

    /// <summary>Saves this index to a file; it is written to a temporary file first, and then moved.</summary>
    public void Save(string path)
    {
        var tempPath = path + ".tmp";
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16)) {
            using (var header = new BinaryWriter(fs, Encoding.UTF8, true)) {
                header.Write(Magic);
                header.Write(FormatVersion);
                header.Write(this.VersionKey);
                header.Write((int) this.Language);
            }

            using var compressed = new ZLibStream(fs, CompressionLevel.Fastest, true);
            using var w = new BinaryWriter(compressed, Encoding.UTF8, true);
            w.Write(this.SheetNames.Count);
            foreach (var n in this.SheetNames)
                w.Write(n);
            WriteArray(w, this._sheetHasSubrows);
            WriteArray(w, this._lower);
            WriteArray(w, this._lowerOffsets);
            WriteArray(w, this._text);
            WriteArray(w, this._textOffsets);
            WriteArray(w, this._cellOffsets);
            WriteArray(w, this._cellSheets);
            WriteArray(w, this._cellRows);
            WriteArray(w, this._cellSubrows);
            WriteArray(w, this._cellColumns);
            WriteArray(w, this._derivedCells);
            WriteArray(w, this._derivedSources);
            w.Write(this._sourceTexts.Length);
            foreach (var s in this._sourceTexts)
                w.Write(s);
            w.Write(Magic);
        }

        File.Move(tempPath, path, true);
    }

    /// <summary>Loads an index saved with <see cref="Save"/>.</summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="versionKey">Expected version key of the game data.</param>
    /// <param name="language">Expected preferred language.</param>
    /// <returns>The index, or <c>null</c> if the file does not exist, is invalid, or is for another version or
    /// language.</returns>
    public static ExcelValueIndex? TryLoad(string path, string versionKey, Language language)
    {
        try {
            if (!File.Exists(path))
                return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using (var header = new BinaryReader(fs, Encoding.UTF8, true)) {
                if (header.ReadUInt32() != Magic ||
                    header.ReadInt32() != FormatVersion ||
                    header.ReadString() != versionKey ||
                    header.ReadInt32() != (int) language)
                    return null;
            }

            using var compressed = new ZLibStream(fs, CompressionMode.Decompress, true);
            using var bs = new BufferedStream(compressed, 1 << 16);
            using var r = new BinaryReader(bs, Encoding.UTF8, true);
            var sheetNames = new string[r.ReadInt32()];
            for (var i = 0; i < sheetNames.Length; i++)
                sheetNames[i] = r.ReadString();
            var sheetHasSubrows = ReadArray<bool>(r);
            var lower = ReadArray<byte>(r);
            var lowerOffsets = ReadArray<int>(r);
            var text = ReadArray<byte>(r);
            var textOffsets = ReadArray<int>(r);
            var cellOffsets = ReadArray<int>(r);
            var cellSheets = ReadArray<ushort>(r);
            var cellRows = ReadArray<uint>(r);
            var cellSubrows = ReadArray<ushort>(r);
            var cellColumns = ReadArray<ushort>(r);
            var derivedCells = ReadArray<int>(r);
            var derivedSources = ReadArray<int>(r);
            var sourceTextCount = r.ReadInt32();
            if (sourceTextCount < 0)
                return null;
            var sourceTexts = new string[sourceTextCount];
            for (var i = 0; i < sourceTexts.Length; i++)
                sourceTexts[i] = r.ReadString();
            if (r.ReadUInt32() != Magic)
                return null;

            if (sheetHasSubrows.Length != sheetNames.Length ||
                lowerOffsets.Length == 0 ||
                lowerOffsets.Length != textOffsets.Length ||
                lowerOffsets.Length != cellOffsets.Length ||
                lowerOffsets[^1] != lower.Length ||
                textOffsets[^1] != text.Length ||
                cellOffsets[^1] != cellRows.Length ||
                cellSheets.Length != cellRows.Length ||
                cellSubrows.Length != cellRows.Length ||
                cellColumns.Length != cellRows.Length ||
                derivedSources.Length != derivedCells.Length ||
                derivedCells.Any(x => (uint) x >= (uint) cellRows.Length) ||
                derivedSources.Any(x => (uint) x >= (uint) sourceTexts.Length))
                return null;

            return new(
                versionKey,
                language,
                sheetNames,
                sheetHasSubrows,
                lower,
                lowerOffsets,
                text,
                textOffsets,
                cellOffsets,
                cellSheets,
                cellRows,
                cellSubrows,
                cellColumns,
                derivedCells,
                derivedSources,
                sourceTexts);
        } catch (Exception e) when (e is IOException or InvalidDataException or EndOfStreamException
                                        or UnauthorizedAccessException or OverflowException
                                        or OutOfMemoryException) {
            return null;
        }
    }

    private static void WriteArray<T>(BinaryWriter w, T[] array) where T : unmanaged
    {
        w.Write(array.Length);
        w.Write(MemoryMarshal.AsBytes(array.AsSpan()));
    }

    private static T[] ReadArray<T>(BinaryReader r) where T : unmanaged
    {
        var length = r.ReadInt32();
        if (length < 0)
            throw new InvalidDataException();
        var array = new T[length];
        var bytes = MemoryMarshal.AsBytes(array.AsSpan());
        while (!bytes.IsEmpty) {
            var read = r.Read(bytes);
            if (read == 0)
                throw new EndOfStreamException();
            bytes = bytes[read..];
        }

        return array;
    }

    /// <summary>Converts a value to the form stored in the lowercased blob.</summary>
    internal static byte[] ToLowerBytes(string value) => Encoding.UTF8.GetBytes(value.ToLowerInvariant());

    /// <summary>A game file or folder that values may refer to; see <see cref="FindReferences"/>.</summary>
    private sealed class ReferenceTarget {
        private readonly bool _isFolder;
        private readonly byte[] _path;
        private readonly byte[]? _stemPath;
        private readonly byte[] _name;
        private readonly byte[]? _stemName;
        private readonly int[] _slashes;

        /// <param name="path">Lowercase path without leading and trailing slashes.</param>
        /// <param name="isFolder">Whether the path is a folder.</param>
        public ReferenceTarget(string path, bool isFolder)
        {
            this._isFolder = isFolder;
            this._path = Encoding.UTF8.GetBytes(path);
            var name = path[(path.LastIndexOf('/') + 1)..];
            this._name = Encoding.UTF8.GetBytes(name);
            this._slashes = Enumerable.Range(0, path.Length).Where(i => path[i] == '/').ToArray();

            var dot = name.LastIndexOf('.');
            if (!isFolder && dot > 0) {
                var stemName = name[..dot];
                this._stemPath = Encoding.UTF8.GetBytes(path[..(path.Length - name.Length + dot)]);

                // A bare name such as "levitate0f" is a reference, but a word such as "circle" may be anything.
                if (stemName.Length >= 4 && stemName.Any(c => c == '_' || char.IsAsciiDigit(c)))
                    this._stemName = Encoding.UTF8.GetBytes(stemName);
                this.Needle = Encoding.UTF8.GetBytes(stemName);
            } else {
                this.Needle = this._name;
            }
        }

        /// <summary>The part that every matching value contains.</summary>
        public byte[] Needle { get; }

        /// <summary>Tests if a value refers to this path.</summary>
        /// <returns>0 for the path itself, 1 for a path in the folder, 2 for the end of the path, 3 for the name
        /// alone; -1 if the value does not refer to this path.</returns>
        public int Classify(ReadOnlySpan<byte> value)
        {
            if (value.StartsWith("/"u8))
                value = value[1..];

            if (this._isFolder) {
                var trimmed = value.EndsWith("/"u8) ? value[..^1] : value;
                if (trimmed.SequenceEqual(this._path))
                    return 0;
                if (value.Length > this._path.Length && value.StartsWith(this._path) &&
                    value[this._path.Length] == (byte) '/')
                    return 1;
                if (trimmed.IndexOf((byte) '/') >= 0 && IsSuffixAtBoundary(this._path, trimmed))
                    return 2;

                // A partial path of something in the folder, such as "2ax_warrior/abl001" in "…/2ax_warrior/".
                for (var i = 0; i < this._slashes.Length - 1; i++) {
                    var suffix = this._path.AsSpan(this._slashes[i] + 1);
                    if (value.Length > suffix.Length && value.StartsWith(suffix) && value[suffix.Length] == (byte) '/')
                        return 2;
                }

                return -1;
            }

            if (value.SequenceEqual(this._path) || (this._stemPath is { } sp && value.SequenceEqual(sp)))
                return 0;
            if (value.IndexOf((byte) '/') >= 0) {
                return IsSuffixAtBoundary(this._path, value) ||
                    (this._stemPath is { } stemPath && IsSuffixAtBoundary(stemPath, value))
                        ? 2
                        : -1;
            }

            if (value.SequenceEqual(this._name) || (this._stemName is { } sn && value.SequenceEqual(sn)))
                return 3;
            return -1;
        }

        private static bool IsSuffixAtBoundary(ReadOnlySpan<byte> whole, ReadOnlySpan<byte> suffix) =>
            suffix.Length < whole.Length &&
            whole.EndsWith(suffix) &&
            whole[whole.Length - suffix.Length - 1] == (byte) '/';
    }

    /// <summary>A search term, along with the looser forms that a term that looks like a file path may match in.
    /// </summary>
    /// <remarks>
    /// Sheets often refer to files without their extension or directory: <c>bgm_system_title.scd</c> should find
    /// <c>music/ffxiv/BGM_System_Title.scd</c>, and <c>vfx/common/eff/levitate0f.avfx</c> should find
    /// <c>levitate0f</c>. The looser forms must not run into more of a name (<c>levitate0f</c> does not match
    /// <c>levitate0f2</c>).
    /// </remarks>
    private sealed class SearchTerm {
        public SearchTerm(string term)
        {
            this.Full = ToLowerBytes(term.Trim());
            var slash = Array.LastIndexOf(this.Full, (byte) '/');
            var dot = Array.LastIndexOf(this.Full, (byte) '.');
            var hasExtension = dot > slash + 1 && this.Full.Length - dot - 1 is >= 1 and <= 5 &&
                this.Full.AsSpan(dot + 1).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789"u8) < 0;

            this.Name = this.Full[(slash + 1)..];
            if (hasExtension)
                this.Stem = this.Full[..dot];
            this.NameStem = hasExtension ? this.Full[(slash + 1)..dot] : this.Name;
            if (slash >= 0 && this.NameStem.Length > 0)
                this.LooseName = this.NameStem;
            this.Required = this.LooseName ?? this.Stem ?? this.Full;
        }

        /// <summary>The whole term, lowercased.</summary>
        public byte[] Full { get; }

        /// <summary>The part after the last slash.</summary>
        public byte[] Name { get; }

        /// <summary>The part after the last slash, without the extension.</summary>
        public byte[] NameStem { get; }

        /// <summary>The term without its file extension, if it looks like it has one.</summary>
        private byte[]? Stem { get; }

        /// <summary>The name without directories and extension, if the term has directories.</summary>
        private byte[]? LooseName { get; }

        /// <summary>The part that must appear in a matching value.</summary>
        public byte[] Required { get; }

        public bool IsMatch(ReadOnlySpan<byte> value) =>
            value.IndexOf(this.Full) >= 0 ||
            (this.Stem is { } stem && ContainsBounded(value, stem, false)) ||
            (this.LooseName is { } looseName && ContainsBounded(value, looseName, true));

        private static bool ContainsBounded(ReadOnlySpan<byte> value, ReadOnlySpan<byte> part, bool boundedBefore)
        {
            for (var position = 0; position < value.Length;) {
                var found = value[position..].IndexOf(part);
                if (found < 0)
                    return false;
                found += position;
                var after = found + part.Length;
                if ((!boundedBefore || found == 0 || !IsNameChar(value[found - 1])) &&
                    (after >= value.Length || !IsNameChar(value[after])))
                    return true;
                position = found + 1;
            }

            return false;
        }

        private static bool IsNameChar(byte b) =>
            b is >= (byte) 'a' and <= (byte) 'z' or >= (byte) '0' and <= (byte) '9' or (byte) '_' or (byte) '-';
    }
}
