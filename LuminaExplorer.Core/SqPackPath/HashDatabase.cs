using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Misc;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.SqPackPath;

public class HashDatabase {
    private readonly FolderStruct[] _folders;
    private readonly FileStruct[] _files;
    private readonly byte[] _strings;

    public HashDatabase(FileInfo cachedFile)
    {
        // FileInfo caches its state; the file may have been created after the caller first queried it.
        cachedFile.Refresh();
        if (!cachedFile.Exists) {
            this._folders = [];
            this._files = [];
            this._strings = [];
        } else {
            using (var readerCompressed = new BinaryReader(cachedFile.OpenRead())) {
                this._strings = new byte[readerCompressed.ReadInt32()];
                using (var readerDecompressed = new ZLibStream(readerCompressed.BaseStream, CompressionMode.Decompress))
                using (var ms = new MemoryStream(this._strings))
                    readerDecompressed.CopyTo(ms);
            }

            using var reader = new BinaryReader(new MemoryStream(this._strings));
            var folderOffset = reader.ReadInt32();
            var fileOffset = reader.ReadInt32();
            var endOffset = reader.ReadInt32();

            reader.BaseStream.Position = folderOffset;
            this._folders = new FolderStruct[(fileOffset - folderOffset) / Unsafe.SizeOf<FolderStruct>()];
            this._files = new FileStruct[(endOffset - fileOffset) / Unsafe.SizeOf<FileStruct>()];
            unsafe {
                fixed (void* b = this._folders)
                    reader.BaseStream.ReadExactly(new(b, fileOffset - folderOffset));
                fixed (void* b = this._files)
                    reader.BaseStream.ReadExactly(new(b, endOffset - fileOffset));
            }

            this._strings = this._strings[..folderOffset];
        }
    }

    public FolderStruct? GetFolderEntry(uint indexId, uint hash)
    {
        var i = Array.BinarySearch(
            this._folders,
            new() {
                IndexId = indexId,
                Hash = hash,
            });
        if (i < 0)
            return null;

        return this._folders[i];
    }

    public string GetString(int offset)
    {
        var length = 0;
        while (this._strings[offset + length] != 0)
            length++;

        return Encoding.UTF8.GetString(this._strings, offset, length);
    }

    public string? GetFileName(FolderStruct folder, uint hash)
    {
        var i = Array.BinarySearch(
            this._files,
            folder.FileIndex,
            folder.FileCount,
            new() {
                Hash = hash,
            });
        return i < 0 ? null : this.GetString(this._files[i].NameOffset);
    }

    public string? FindFileName(uint indexId, uint hash)
    {
        var folderFrom = Array.BinarySearch(
            this._folders,
            new() {
                IndexId = indexId,
                Hash = uint.MinValue,
            });
        var folderTo = Array.BinarySearch(
            this._folders,
            new() {
                IndexId = indexId,
                Hash = uint.MaxValue,
            });
        if (folderFrom < 0)
            folderFrom = ~folderFrom;
        // Make folderTo exclusive.
        if (folderTo < 0)
            folderTo = ~folderTo;
        else
            folderTo++;

        var compareFile = new FileStruct {
            Hash = hash,
        };
        for (var folderIndex = folderFrom; folderIndex < folderTo; folderIndex++) {
            var i = Array.BinarySearch(
                this._files,
                this._folders[folderIndex].FileIndex,
                this._folders[folderIndex].FileCount,
                compareFile);
            if (i >= 0)
                return this.GetString(this._files[i].NameOffset);
        }

        return null;
    }

    private NameSearchIndex? _nameSearchIndex;

    /// <summary>
    /// Finds known folders and files whose name contains all of the given terms, ignoring case.
    /// Folders are returned with a trailing slash, and include intermediate folders that only appear as a part of
    /// longer paths.
    /// </summary>
    /// <param name="terms">Terms that must all appear in the same name.</param>
    /// <param name="maxResults">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching paths, without a leading slash.</returns>
    public List<string> FindPathsByName(
        IReadOnlyList<string> terms,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        var lowerTerms = terms
            .Where(x => x.Length > 0)
            .Select(x => Encoding.UTF8.GetBytes(x.ToLowerInvariant()))
            .OrderByDescending(x => x.Length)
            .ToArray();
        if (lowerTerms.Length == 0 || this._strings.Length == 0)
            return results;

        var index = this.GetNameSearchIndex();
        var haystack = index.LowerStrings.AsSpan();
        var needle = lowerTerms[0];
        var seen = new HashSet<string>();

        var position = 0;
        while (position < haystack.Length && results.Count < maxResults) {
            cancellationToken.ThrowIfCancellationRequested();

            var found = haystack[position..].IndexOf(needle);
            if (found < 0)
                break;
            found += position;

            var stringStart = haystack[..found].LastIndexOf((byte) 0) + 1;
            var stringEnd = haystack[found..].IndexOf((byte) 0);
            stringEnd = stringEnd < 0 ? haystack.Length : found + stringEnd;
            position = found + 1;

            // Only match within a single path component.
            var componentStart = haystack[stringStart..found].LastIndexOf((byte) '/') + 1 + stringStart;
            var componentEnd = haystack[found..stringEnd].IndexOf((byte) '/');
            componentEnd = componentEnd < 0 ? stringEnd : found + componentEnd;
            var component = haystack[componentStart..componentEnd];
            var allTermsFound = true;
            for (var i = 1; i < lowerTerms.Length && allTermsFound; i++)
                allTermsFound = component.IndexOf(lowerTerms[i]) >= 0;
            if (!allTermsFound)
                continue;

            if (componentEnd < stringEnd) {
                // A folder in the middle of a folder path.
                var path = Encoding.UTF8.GetString(this._strings, stringStart, componentEnd - stringStart) + "/";
                if (seen.Add(path))
                    results.Add(path);
                continue;
            }

            // Whole folder paths, and file names in the folders that reference this string.
            foreach (var folderIndex in index.FoldersByNameOffset(stringStart)) {
                var path = this.GetString(this._folders[folderIndex].NameOffset) + "/";
                if (seen.Add(path))
                    results.Add(path);
            }

            foreach (var fileIndex in index.FilesByNameOffset(stringStart)) {
                if (results.Count >= maxResults)
                    break;
                var folderPath = this.GetString(this._folders[index.FolderOfFile[fileIndex]].NameOffset);
                var path = $"{folderPath}/{this.GetString(this._files[fileIndex].NameOffset)}";
                if (seen.Add(path))
                    results.Add(path);
            }

            // Each string needs to be reported only once.
            position = Math.Max(position, componentEnd);
        }

        return results;
    }

    private NameSearchIndex GetNameSearchIndex()
    {
        if (this._nameSearchIndex is { } index)
            return index;
        lock (this._folders)
            return this._nameSearchIndex ??= new(this);
    }

    /// <summary>Gets the known names of the folders that contain files, such as <c>chara/monster/m0361/obj/body/b0001</c>.
    /// </summary>
    public IEnumerable<(uint IndexId, uint Hash, string Path)> EnumerateFolders()
    {
        foreach (var folder in this._folders)
            yield return (folder.IndexId, folder.Hash, this.GetString(folder.NameOffset));
    }

    /// <summary>
    /// Finds known files and folders whose full path ends with the given partial path, at a path component boundary.
    /// </summary>
    /// <param name="partialPath">Lowercase partial path without a leading slash, such as
    /// <c>ability/2ax_warrior/abl001</c>, <c>levitate0f</c>, or <c>battle/etc/se_bt_etc_unicornvo.scd</c>.</param>
    /// <param name="anyExtension">Whether the last component is a name without an extension, which files with any
    /// extension may have; folders are matched only then.</param>
    /// <param name="maxResults">Maximum number of results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching paths, without a leading slash; folders end with a slash.</returns>
    public List<string> FindPathsBySuffix(
        string partialPath,
        bool anyExtension,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        partialPath = partialPath.Trim('/');
        if (partialPath.Length == 0 || this._strings.Length == 0)
            return results;

        var index = this.GetNameSearchIndex();
        var haystack = index.LowerStrings.AsSpan();
        var slash = partialPath.LastIndexOf('/');
        var dir = slash < 0 ? string.Empty : partialPath[..slash];
        var name = Encoding.UTF8.GetBytes(partialPath[(slash + 1)..]);

        // Files: the name, followed by the end of the string or an extension.
        for (var position = 0; position < haystack.Length && results.Count < maxResults;) {
            cancellationToken.ThrowIfCancellationRequested();
            var found = haystack[position..].IndexOf(name);
            if (found < 0)
                break;
            found += position;
            position = found + 1;
            if (found > 0 && haystack[found - 1] != 0)
                continue;

            var end = haystack[found..].IndexOf((byte) 0);
            end = end < 0 ? haystack.Length : found + end;
            var rest = haystack[(found + name.Length)..end];
            if (rest.Length != 0) {
                if (!anyExtension || rest[0] != (byte) '.' || rest.Length > 8 ||
                    rest[1..].IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789"u8) >= 0)
                    continue;
            }

            foreach (var fileIndex in index.FilesByNameOffset(found)) {
                var folderPath = this.GetString(this._folders[index.FolderOfFile[fileIndex]].NameOffset);
                if (dir.Length != 0 && !IsSuffixAtBoundary(folderPath, dir))
                    continue;
                results.Add($"{folderPath}/{this.GetString(this._files[fileIndex].NameOffset)}");
                if (results.Count >= maxResults)
                    break;
            }
        }

        // Folders containing files.
        if (anyExtension && results.Count < maxResults) {
            var whole = Encoding.UTF8.GetBytes(partialPath);
            for (var position = 0; position < haystack.Length && results.Count < maxResults;) {
                cancellationToken.ThrowIfCancellationRequested();
                var found = haystack[position..].IndexOf(whole);
                if (found < 0)
                    break;
                found += position;
                position = found + 1;
                var after = found + whole.Length;
                if ((found > 0 && haystack[found - 1] is not (0 or (byte) '/')) ||
                    (after < haystack.Length && haystack[after] != 0))
                    continue;

                var stringStart = haystack[..found].LastIndexOf((byte) 0) + 1;
                foreach (var folderIndex in index.FoldersByNameOffset(stringStart)) {
                    results.Add(this.GetString(this._folders[folderIndex].NameOffset) + "/");
                    if (results.Count >= maxResults)
                        break;
                }
            }
        }

        return results;

        static bool IsSuffixAtBoundary(string path, string suffix) =>
            path.Length == suffix.Length
                ? path.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                : path.Length > suffix.Length &&
                path[^(suffix.Length + 1)] == '/' &&
                path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NameSearchIndex {
        public readonly byte[] LowerStrings;
        public readonly int[] FolderOfFile;
        private readonly int[] _foldersSortedByNameOffset;
        private readonly int[] _folderNameOffsets;
        private readonly int[] _filesSortedByNameOffset;
        private readonly int[] _fileNameOffsets;

        public NameSearchIndex(HashDatabase db)
        {
            // Paths are ASCII in practice; lowercase them once so that searches can use a plain byte search.
            this.LowerStrings = (byte[]) db._strings.Clone();
            for (var i = 0; i < this.LowerStrings.Length; i++) {
                if (this.LowerStrings[i] is >= (byte) 'A' and <= (byte) 'Z')
                    this.LowerStrings[i] += 'a' - 'A';
            }

            this.FolderOfFile = new int[db._files.Length];
            for (var i = 0; i < db._folders.Length; i++)
                this.FolderOfFile.AsSpan(db._folders[i].FileIndex, db._folders[i].FileCount).Fill(i);

            this._foldersSortedByNameOffset = Enumerable.Range(0, db._folders.Length).ToArray();
            this._folderNameOffsets = db._folders.Select(x => x.NameOffset).ToArray();
            Array.Sort(this._folderNameOffsets, this._foldersSortedByNameOffset);

            this._filesSortedByNameOffset = Enumerable.Range(0, db._files.Length).ToArray();
            this._fileNameOffsets = db._files.Select(x => x.NameOffset).ToArray();
            Array.Sort(this._fileNameOffsets, this._filesSortedByNameOffset);
        }

        public ReadOnlySpan<int> FoldersByNameOffset(int nameOffset) =>
            EqualRange(this._folderNameOffsets, this._foldersSortedByNameOffset, nameOffset);

        public ReadOnlySpan<int> FilesByNameOffset(int nameOffset) =>
            EqualRange(this._fileNameOffsets, this._filesSortedByNameOffset, nameOffset);

        private static ReadOnlySpan<int> EqualRange(int[] keys, int[] values, int key)
        {
            var i = Array.BinarySearch(keys, key);
            if (i < 0)
                return [];

            var from = i;
            while (from > 0 && keys[from - 1] == key)
                from--;
            var to = i + 1;
            while (to < keys.Length && keys[to] == key)
                to++;
            return values.AsSpan(from, to - from);
        }
    }

    public struct FolderStruct : IComparable<FolderStruct> {
        public int NameOffset;
        public uint IndexId;
        public uint Hash;
        public int FileIndex;
        public int FileCount;

        public int CompareTo(FolderStruct other) =>
            this.IndexId == other.IndexId
                ? this.Hash.CompareTo(other.Hash)
                : this.IndexId.CompareTo(other.IndexId);
    }

    public struct FileStruct : IComparable<FileStruct> {
        public int NameOffset;
        public uint Hash;

        public int CompareTo(FileStruct other) => this.Hash.CompareTo(other.Hash);
    }

    public static async Task MakeCachedFile(
        string sourceUrl,
        Stream target,
        Action<float> progress,
        CancellationToken cancellationToken)
    {
        const float progressWeightConnect = 0.1f;
        const float progressWeightDownload = 0.4f;
        const float progressWeightProcess = 0.5f;

        using var client = new HttpClient();
        using var resp = await client.GetAsync(
            sourceUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var length = resp.Content.Headers.ContentLength;
        await using var countingStream = new CountingStream(await resp.Content.ReadAsStreamAsync(cancellationToken));
        using var reader = new StreamReader(
            new GZipStream(countingStream, CompressionMode.Decompress),
            Encoding.UTF8);

        var indexToFolderMap = new Dictionary<Tuple<uint, uint>, FolderEntry>();

        progress(progressWeightConnect);
        var endOfStream = false;
        while (!endOfStream) {
            for (var i = 0; i < 10000; i++) {
                var line = (await reader.ReadLineAsync(cancellationToken))?.Trim();
                if (line is null) {
                    endOfStream = true;
                    break;
                }

                if (GetIndexId(line) is not { } indexId)
                    continue;

                var sep = line.LastIndexOf('/');
                var folderName = line[..sep];
                var folderHash = Crc32.Get(Encoding.UTF8.GetBytes(folderName.ToLowerInvariant()));
                var fileName = line[(sep + 1)..];
                var fileHash = Crc32.Get(Encoding.UTF8.GetBytes(fileName.ToLowerInvariant()));
                var folderKey = Tuple.Create(indexId, folderHash);

                if (!indexToFolderMap.TryGetValue(folderKey, out var folder))
                    indexToFolderMap.Add(folderKey, folder = new(folderHash, folderName));

                folder.Files.Add(new(fileHash, fileName));
            }

            if (length is { } n)
                progress(progressWeightConnect + progressWeightDownload * ((float) countingStream.ReadCounter / n));
        }

        progress(progressWeightConnect + progressWeightDownload);

        var folders = new FolderStruct[indexToFolderMap.Count];
        var files = new FileStruct[indexToFolderMap.Values.Sum(x => x.Files.Count)];

        var folderIndex = 0;
        var fileIndex = 0;
        await using var outWriter = new BinaryWriter(new MemoryStream());
        outWriter.BaseStream.Write(new byte[12]); // offset to folders, offset to files, offset to end
        var stringOffsets = new Dictionary<string, int>();
        foreach (var ((indexId, folderHash), folder) in indexToFolderMap.OrderBy(x => x.Key)) {
            if (!stringOffsets.TryGetValue(folder.Text, out var nameOffset)) {
                stringOffsets[folder.Text] = nameOffset = checked((int) outWriter.BaseStream.Length);
                outWriter.BaseStream.Write(Encoding.UTF8.GetBytes(folder.Text));
                outWriter.BaseStream.WriteByte(0);
            }

            folders[folderIndex++] = new() {
                NameOffset = nameOffset,
                IndexId = indexId,
                Hash = folderHash,
                FileIndex = fileIndex,
                FileCount = folder.Files.Count,
            };

            foreach (var file in folder.Files.OrderBy(x => x.Hash)) {
                if (!stringOffsets.TryGetValue(file.Text, out nameOffset)) {
                    stringOffsets[file.Text] = nameOffset = checked((int) outWriter.BaseStream.Length);
                    outWriter.BaseStream.Write(Encoding.UTF8.GetBytes(file.Text));
                    outWriter.BaseStream.WriteByte(0);
                }

                files[fileIndex++] = new() {
                    NameOffset = nameOffset,
                    Hash = file.Hash,
                };
            }

            if (folderIndex % 1000 == 0) {
                progress(
                    progressWeightConnect + progressWeightDownload +
                    progressWeightProcess * ((float) folderIndex / folders.Length));
            }
        }

        var padding = outWriter.BaseStream.Position % 4;
        if (padding > 0)
            outWriter.BaseStream.Write(new byte[4 - padding]);

        var folderOffset = checked((int) outWriter.BaseStream.Position);
        var fileOffset = folderOffset + Unsafe.SizeOf<FolderStruct>() * folders.Length;
        var endOffset = fileOffset + Unsafe.SizeOf<FileStruct>() * files.Length;

        outWriter.BaseStream.Position = 0;
        outWriter.Write(folderOffset);
        outWriter.Write(fileOffset);
        outWriter.Write(endOffset);

        outWriter.BaseStream.Position = folderOffset;
        unsafe {
            fixed (void* b = folders)
                outWriter.BaseStream.Write(new(b, fileOffset - folderOffset));
            fixed (void* b = files)
                outWriter.BaseStream.Write(new(b, endOffset - fileOffset));
        }

        using var compressed = new MemoryStream();
        outWriter.BaseStream.Position = 0;
        await using (var compressor = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            await outWriter.BaseStream.CopyToAsync(compressor, cancellationToken);

        await using var fileWriter = new BinaryWriter(target);
        fileWriter.Write((int) outWriter.BaseStream.Length);
        compressed.Position = 0;
        await compressed.CopyToAsync(fileWriter.BaseStream, cancellationToken);
    }

    private class HashEntry {
        public readonly uint Hash;

        public HashEntry(uint hash)
        {
            this.Hash = hash;
        }
    }

    private class FolderEntry : HashEntry {
        public readonly string Text;
        public readonly List<FileEntry> Files = [];

        public FolderEntry(uint folderHash, string text)
            : base(folderHash)
        {
            this.Text = text;
        }

        public override string ToString() => this.Text;
    }

    private class FileEntry : HashEntry {
        public readonly string Text;

        public FileEntry(uint fileHash, string text) : base(fileHash)
        {
            this.Text = text;
        }

        public override string ToString() => this.Text;
    }

    private static uint? GetIndexId(string gamePath)
    {
        var sep = gamePath.IndexOf('/');
        if (sep == -1)
            return null;
        if (!Repository.CategoryNameToIdMap.TryGetValue(gamePath[..sep], out var categoryId))
            return null;
        sep++;

        var exVer = 0u;
        var chunkId = 0u;

        if (categoryId is 0x02 or 0x03 or 0x0c) {
            if (sep + 3 < gamePath.Length && gamePath[sep++] == 'e' && gamePath[sep++] == 'x') {
                for (; sep < gamePath.Length && gamePath[sep] is >= '0' and <= '9'; sep++)
                    exVer = exVer * 10 + gamePath[sep] - '0';

                if (categoryId == 0x02) {
                    sep = gamePath.IndexOf('/', sep);
                    if (sep != -1) {
                        sep++;
                        for (; sep < gamePath.Length && gamePath[sep] is >= '0' and <= '9'; sep++)
                            chunkId = chunkId * 10 + gamePath[sep] - '0';
                    }
                }
            }
        }

        return (uint) categoryId << 16 | exVer << 8 | chunkId;
    }
}
