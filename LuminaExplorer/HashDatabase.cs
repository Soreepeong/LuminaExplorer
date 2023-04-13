using Microsoft.Data.Sqlite;

namespace LuminaExplorer;

public class HashDatabase {
    private readonly string _dbPath;
    private readonly Dictionary<Tuple<uint, uint>, FileEntry> _indexToFileMap = new();
    private readonly Dictionary<Tuple<uint, uint>, FolderEntry> _indexToFolderMap = new();
    private readonly Dictionary<Tuple<uint, uint>, FullPathEntry> _indexToFullMap = new();

    public HashDatabase(string dbPath = @"Z:\GitWorks\ffxiv-explorer-fork\hashlist.db") {
        _dbPath = dbPath;
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

                var file = new FileEntry(indexId, fileHash, fileNames[fileId], folder);
                folder.Files.Add(file);

                var entry = new FullPathEntry(indexId, fullHash, folder, file);

                _indexToFileMap[Tuple.Create(indexId, fileHash)] = file;
                _indexToFolderMap[Tuple.Create(indexId, folderHash)] = folder;
                _indexToFullMap[Tuple.Create(indexId, fullHash)] = entry;
            }
        }
    }

    public FileEntry? GetFileEntry(uint indexId, uint hash) =>
        _indexToFileMap.GetValueOrDefault(Tuple.Create(indexId, hash));

    public FolderEntry? GetFolderEntry(uint indexId, uint hash) =>
        _indexToFolderMap.GetValueOrDefault(Tuple.Create(indexId, hash));

    public FileEntry? GetFileEntry(uint indexId, uint folderHash, uint fileHash) {
        if (GetFolderEntry(indexId, folderHash) is { } folder)
            return folder.Files.First(x => x.Hash == fileHash);
        return GetFileEntry(indexId, fileHash);
    }

    public FullPathEntry? GetFullPathEntry(uint indexId, uint hash) =>
        _indexToFullMap.GetValueOrDefault(Tuple.Create(indexId, hash));

    public class HashEntry {
        public readonly uint IndexId;
        public readonly uint Hash;

        public HashEntry(uint indexId, uint hash) {
            IndexId = indexId;
            Hash = hash;
        }
    }

    public class FolderEntry : HashEntry {
        public readonly string Text;
        public readonly List<FileEntry> Files = new();

        public FolderEntry(uint indexId, uint folderHash, string text)
            : base(indexId, folderHash) {
            Text = text;
        }

        public override string ToString() => Text;
    }

    public class FileEntry : HashEntry {
        public readonly string Text;
        public readonly FolderEntry Parent;

        public FileEntry(uint indexId, uint fileHash, string text, FolderEntry parent) : base(indexId, fileHash) {
            Text = text;
            Parent = parent;
        }

        public override string ToString() => Text;
    }

    public class FullPathEntry : HashEntry {
        public readonly FolderEntry Folder;
        public readonly FileEntry File;

        public FullPathEntry(uint indexId, uint fullHash, FolderEntry folder, FileEntry file) :
            base(indexId, fullHash) {
            Folder = folder;
            File = file;
        }

        public override string ToString() => $"{Folder.Text}/{File.Text}";
    }
}
