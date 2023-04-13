using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LuminaExplorer;

public class HashDatabase {
    private readonly string _dbPath;
    private readonly FolderStruct[] _folders;
    private readonly FileStruct[] _files;
    private readonly byte[] _strings;

    public HashDatabase(string dbPath = @"Z:\GitWorks\ffxiv-explorer-fork\hashlist.db") {
        _dbPath = dbPath;

        var cachedFile = new FileInfo("hashlist.cache");
        if (!cachedFile.Exists) {
            using var conn = new SqliteConnection($"Data Source={new Uri(_dbPath).AbsoluteUri}; mode=ReadOnly");
            conn.Open();
            var cmd = conn.CreateCommand();

            var fileNames = new Dictionary<ulong, string>();
            cmd.CommandText = "SELECT * FROM filenames";
            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read())
                    fileNames.Add(unchecked((ulong) reader.GetFieldValue<long>(0)), reader.GetFieldValue<string>(1));
            }

            var folderNames = new Dictionary<ulong, string>();
            cmd.CommandText = "SELECT * FROM folders";
            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read())
                    folderNames.Add(unchecked((ulong) reader.GetFieldValue<long>(0)), reader.GetFieldValue<string>(1));
            }

            var indexToFolderMap = new Dictionary<Tuple<uint, uint>, FolderEntry>();
            var folderEntries = new Dictionary<ulong, FolderEntry>();
            cmd.CommandText = "select indexid, fullhash, folderhash, filehash, folder, file from fullpaths";
            using (var reader = cmd.ExecuteReader()) {
                while (reader.Read()) {
                    var indexId = unchecked((uint) reader.GetFieldValue<int>(0));
                    var fullHash = unchecked((uint) reader.GetFieldValue<int>(1));
                    var folderHash = unchecked((uint) reader.GetFieldValue<int>(2));
                    var fileHash = unchecked((uint) reader.GetFieldValue<int>(3));
                    var folderId = unchecked((ulong) reader.GetFieldValue<long>(4));
                    var fileId = unchecked((ulong) reader.GetFieldValue<long>(5));

                    if (!folderEntries.TryGetValue(folderId, out var folder)) {
                        folder = new(indexId, folderHash, folderNames[folderId]);
                        folderEntries.Add(folderId, folder);
                    }

                    var file = new FileEntry(indexId, fileHash, fileNames[fileId]);
                    folder.Files.Add(file);

                    indexToFolderMap[Tuple.Create(indexId, folderHash)] = folder;
                }
            }

            _folders = new FolderStruct[indexToFolderMap.Count];
            _files = new FileStruct[indexToFolderMap.Values.Sum(x => x.Files.Count)];

            var folderIndex = 0;
            var fileIndex = 0;
            using var outWriter = new BinaryWriter(new MemoryStream());
            outWriter.BaseStream.Write(new byte[12]); // offset to folders, offset to files, offset to end
            var stringOffsets = new Dictionary<string, int>();
            foreach (var ((indexId, folderHash), folder) in indexToFolderMap.OrderBy(x => x.Key)) {
                if (!stringOffsets.TryGetValue(folder.Text, out var nameOffset)) {
                    stringOffsets[folder.Text] = nameOffset = checked((int) outWriter.BaseStream.Length);
                    outWriter.BaseStream.Write(Encoding.UTF8.GetBytes(folder.Text));
                    outWriter.BaseStream.WriteByte(0);
                }

                _folders[folderIndex++] = new() {
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

                    _files[fileIndex++] = new() {
                        NameOffset = nameOffset,
                        Hash = file.Hash,
                    };
                }
            }

            var padding = outWriter.BaseStream.Position % 4;
            if (padding > 0)
                outWriter.BaseStream.Write(new byte[padding]);

            var folderOffset = checked((int) outWriter.BaseStream.Position);
            var fileOffset = folderOffset + Marshal.SizeOf<FolderStruct>() * _folders.Length;
            var endOffset = fileOffset + Marshal.SizeOf<FileStruct>() * _files.Length;

            outWriter.BaseStream.Position = 0;
            _strings = new byte[folderOffset];
            outWriter.BaseStream.Read(_strings);
            
            outWriter.BaseStream.Position = 0;
            outWriter.Write(folderOffset);
            outWriter.Write(fileOffset);
            outWriter.Write(endOffset);

            outWriter.BaseStream.Position = folderOffset;
            unsafe {
                fixed (void* b = _folders)
                    outWriter.BaseStream.Write(new(b, fileOffset - folderOffset));
                fixed (void* b = _files)
                    outWriter.BaseStream.Write(new(b, endOffset - fileOffset));
            }

            using var compressed = new MemoryStream();
            outWriter.BaseStream.Position = 0;
            using(var compresser = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                outWriter.BaseStream.CopyTo(compresser);

            using var fileWriter = new BinaryWriter(cachedFile.Open(FileMode.Create, FileAccess.ReadWrite));
            fileWriter.Write((int)outWriter.BaseStream.Length);
            compressed.Position = 0;
            compressed.CopyTo(fileWriter.BaseStream);
            
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
            _folders = new FolderStruct[(fileOffset - folderOffset) / Marshal.SizeOf<FolderStruct>()];
            _files = new FileStruct[(endOffset - fileOffset) / Marshal.SizeOf<FileStruct>()];
            unsafe {
                fixed (void* b = _folders)
                    reader.BaseStream.Read(new(b, fileOffset - folderOffset));
                fixed (void* b = _files)
                    reader.BaseStream.Read(new(b, endOffset - fileOffset));
            }

            _strings = _strings[..folderOffset];
        }
    }

    public FolderStruct? GetFolderEntry(uint indexId, uint hash) {
        var i = Array.BinarySearch(_folders, new() {IndexId = indexId, Hash = hash});
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
        var i = Array.BinarySearch(_files, folder.FileIndex, folder.FileCount, new() {Hash = hash});
        return i < 0 ? null : GetString(_files[i].NameOffset);
    }

    public string? FindFileName(uint indexId, uint hash) {
        var folderFrom = Array.BinarySearch(_folders, new() {IndexId = indexId, Hash = uint.MinValue});
        var folderTo = Array.BinarySearch(_folders, new() {IndexId = indexId, Hash = uint.MaxValue});
        if (folderFrom < 0)
            folderFrom = ~folderFrom;
        if (folderTo < 0)
            folderTo = ~folderTo;

        var compareFile = new FileStruct {Hash = hash};
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

    private class HashEntry {
        public readonly uint IndexId;
        public readonly uint Hash;

        public HashEntry(uint indexId, uint hash) {
            IndexId = indexId;
            Hash = hash;
        }
    }

    private class FolderEntry : HashEntry {
        public readonly string Text;
        public readonly List<FileEntry> Files = new();

        public FolderEntry(uint indexId, uint folderHash, string text)
            : base(indexId, folderHash) {
            Text = text;
        }

        public override string ToString() => Text;
    }

    private class FileEntry : HashEntry {
        public readonly string Text;

        public FileEntry(uint indexId, uint fileHash, string text) : base(indexId, fileHash) {
            Text = text;
        }

        public override string ToString() => Text;
    }
}
