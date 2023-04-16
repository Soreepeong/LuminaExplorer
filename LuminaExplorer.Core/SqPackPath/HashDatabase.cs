using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Lumina.Data;
using Lumina.Misc;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.SqPackPath;

public class HashDatabase {
    private const string PathListUrl = "https://rl2.perchbird.dev/download/export/PathList.gz";
    private readonly FolderStruct[] _folders;
    private readonly FileStruct[] _files;
    private readonly byte[] _strings;

    public HashDatabase(FileInfo cachedFile) {
        if (!cachedFile.Exists) {
            _folders = Array.Empty<FolderStruct>();
            _files = Array.Empty<FileStruct>();
            _strings = Array.Empty<byte>();
        } else {
            using (var readerCompressed = new BinaryReader(cachedFile.OpenRead())) {
                _strings = new byte[readerCompressed.ReadInt32()];
                using (var readerDecompressed = new ZLibStream(readerCompressed.BaseStream, CompressionMode.Decompress))
                using (var ms = new MemoryStream(_strings))
                    readerDecompressed.CopyTo(ms);
            }

            using var reader = new BinaryReader(new MemoryStream(_strings));
            var folderOffset = reader.ReadInt32();
            var fileOffset = reader.ReadInt32();
            var endOffset = reader.ReadInt32();

            reader.BaseStream.Position = folderOffset;
            _folders = new FolderStruct[(fileOffset - folderOffset) / Unsafe.SizeOf<FolderStruct>()];
            _files = new FileStruct[(endOffset - fileOffset) / Unsafe.SizeOf<FileStruct>()];
            unsafe {
                fixed (void* b = _folders)
                    reader.ReadFully(new(b, fileOffset - folderOffset));
                fixed (void* b = _files)
                    reader.ReadFully(new(b, endOffset - fileOffset));
            }

            _strings = _strings[..folderOffset];
        }
    }

    public FolderStruct? GetFolderEntry(uint indexId, uint hash) {
        var i = Array.BinarySearch(_folders, new() {
            IndexId = indexId,
            Hash = hash
        });
        if (i < 0)
            return null;

        return _folders[i];
    }

    public string GetString(int offset) {
        var length = 0;
        while (_strings[offset + length] != 0)
            length++;

        return Encoding.UTF8.GetString(_strings, offset, length);
    }

    public string? GetFileName(FolderStruct folder, uint hash) {
        var i = Array.BinarySearch(_files, folder.FileIndex, folder.FileCount, new() {
            Hash = hash
        });
        return i < 0 ? null : GetString(_files[i].NameOffset);
    }

    public string? FindFileName(uint indexId, uint hash) {
        var folderFrom = Array.BinarySearch(_folders, new() {
            IndexId = indexId,
            Hash = uint.MinValue,
        });
        var folderTo = Array.BinarySearch(_folders, new() {
            IndexId = indexId,
            Hash = uint.MaxValue,
        });
        if (folderFrom < 0)
            folderFrom = ~folderFrom;
        if (folderTo < 0)
            folderTo = ~folderTo;

        var compareFile = new FileStruct {
            Hash = hash
        };
        for (var folderIndex = folderFrom; folderIndex <= folderTo; folderIndex++) {
            var i = Array.BinarySearch(
                _files,
                _folders[folderIndex].FileIndex,
                _folders[folderIndex].FileCount,
                compareFile);
            if (i >= 0)
                return GetString(_files[i].NameOffset);
        }

        return null;
    }

    public struct FolderStruct : IComparable<FolderStruct> {
        public int NameOffset;
        public uint IndexId;
        public uint Hash;
        public int FileIndex;
        public int FileCount;

        public int CompareTo(FolderStruct other) => IndexId == other.IndexId
            ? Hash.CompareTo(other.Hash)
            : IndexId.CompareTo(other.IndexId);
    }

    public struct FileStruct : IComparable<FileStruct> {
        public int NameOffset;
        public uint Hash;

        public int CompareTo(FileStruct other) => Hash.CompareTo(other.Hash);
    }

    public static async Task WriteCachedFile(
        Stream target,
        Action<float> progress,
        CancellationToken cancellationToken) {
        const float progressWeightConnect = 0.1f;
        const float progressWeightDownload = 0.4f;
        const float progressWeightProcess = 0.5f;

        using var client = new HttpClient();
        using var resp = await client.GetAsync(
            PathListUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var length = resp.Content.Headers.ContentLength;
        await using var countingStream = new CountingStream(await resp.Content.ReadAsStreamAsync(cancellationToken));
        using var reader = new StreamReader(
            new GZipStream(countingStream, CompressionMode.Decompress),
            Encoding.UTF8);

        var indexToFolderMap = new Dictionary<Tuple<uint, uint>, FolderEntry>();

        progress(progressWeightConnect);
        while (!reader.EndOfStream) {
            for (var i = 0; i < 10000; i++) {
                var line = (await reader.ReadLineAsync(cancellationToken))?.Trim();
                if (line is null)
                    break;

                if (GetIndexId(line) is not { } indexId)
                    continue;

                var sep = line.LastIndexOf('/');
                var folderName = line[..sep];
                var folderHash = Crc32.Get(folderName.ToLowerInvariant());
                var fileName = line[(sep + 1)..];
                var fileHash = Crc32.Get(fileName.ToLowerInvariant());
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
                progress(progressWeightConnect + progressWeightDownload +
                         progressWeightProcess * ((float) folderIndex / folders.Length));
            }
        }

        var padding = outWriter.BaseStream.Position % 4;
        if (padding > 0)
            outWriter.BaseStream.Write(new byte[padding]);

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

        public HashEntry(uint hash) {
            Hash = hash;
        }
    }

    private class FolderEntry : HashEntry {
        public readonly string Text;
        public readonly List<FileEntry> Files = new();

        public FolderEntry(uint folderHash, string text)
            : base(folderHash) {
            Text = text;
        }

        public override string ToString() => Text;
    }

    private class FileEntry : HashEntry {
        public readonly string Text;

        public FileEntry(uint fileHash, string text) : base(fileHash) {
            Text = text;
        }

        public override string ToString() => Text;
    }

    private static uint? GetIndexId(string gamePath) {
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
                for (; sep < gamePath.Length && gamePath[sep] is > '0' and <= '9'; sep++)
                    exVer = exVer * 10 + gamePath[sep] - '0';

                if (categoryId == 0x02) {
                    sep = gamePath.IndexOf('/', sep);
                    if (sep != -1) {
                        sep++;
                        for (; sep < gamePath.Length && gamePath[sep] is > '0' and <= '9'; sep++)
                            chunkId = chunkId * 10 + gamePath[sep] - '0';
                    }
                }
            }
        }

        return (uint) categoryId << 16 | exVer << 8 | chunkId;
    }
}
