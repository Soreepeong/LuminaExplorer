using System.Runtime.InteropServices;
using System.Text;
using Lumina;
using Lumina.Data;

namespace LuminaExplorer.LazySqPackTree;

public class VirtualFolder {
    public HashDatabase.FolderEntry? KnownEntry;
    public readonly VirtualFolder? Parent;
    public readonly Dictionary<string, VirtualFolder> Folders = new();
    public readonly List<VirtualFile> Files = new();
    public readonly string Name;
    public readonly bool NameUnknown;

    private Action<VirtualFolder>? _resolver;
    private Task _resolverTask = Task.CompletedTask;

    public VirtualFolder(string name, VirtualFolder? parent) {
        Name = name;
        NameUnknown = false;
        Parent = parent;
    }

    public VirtualFolder(int chunk, uint hash, VirtualFolder? parent) {
        Name = $"~{chunk:X02}~{hash:X08}";
        NameUnknown = true;
        Parent = parent;
    }

    internal VirtualFolder GetOrCreateSubfolder(string path) {
        var sepOffset = path.IndexOf('/');
        var name = sepOffset == -1 ? path : path[..sepOffset];
        if (!Folders.TryGetValue(name, out var subfolder))
            Folders.Add(name, subfolder = new(name, this));
        if (sepOffset != -1)
            subfolder = subfolder.GetOrCreateSubfolder(path[(sepOffset + 1)..]);
        return subfolder;
    }

    public bool IsResolved() => _resolver == null && _resolverTask.IsCompleted;

    public void Resolve(Action<VirtualFolder> onCompleteCallback) {
        if (_resolver is not null) {
            var r = _resolver;
            _resolverTask = Task.Run(() => r(this));
            _resolver = null;
        }
        
        _resolverTask.ContinueWith(_ => onCompleteCallback(this));
    }

    public static VirtualFolder CreateRoot(HashDatabase hashDatabase, GameData gameData) {
        var folder = new VirtualFolder("", null);
        folder._resolver = _ => {
            foreach (var (categoryId, categoryName) in Repository.CategoryIdToNameMap) {
                var repos = gameData.Repositories
                    .Where(x => x.Value.Categories.GetValueOrDefault(categoryId)?.Count is > 0)
                    .ToDictionary(x => x.Key, x => x.Value.Categories[categoryId]);
                switch (repos.Count) {
                    case 1:
                        folder.GetOrCreateSubfolder(categoryName)._resolver =
                            ResolverFrom(hashDatabase, categoryName, repos.First().Value);
                        break;
                    case > 1: {
                        var categoryNode = folder.GetOrCreateSubfolder(categoryName);
                        foreach (var (repoName, chunks) in repos) {
                            categoryNode.GetOrCreateSubfolder(repoName)._resolver =
                                ResolverFrom(hashDatabase, $"{categoryName}/{repoName}", chunks);
                        }

                        break;
                    }
                }
            }
        };

        return folder;
    }

    private static Action<VirtualFolder> ResolverFrom(HashDatabase hashDatabase, string expectedPathPrefix,
        List<Category> chunks) {
        return currentFolder => {
            var unknownFolders = new Dictionary<Tuple<int, uint>, VirtualFolder>();
            foreach (var category in chunks) {
                var indexId = (uint) ((category.CategoryId << 16) | (category.Expansion << 8) | category.Chunk);

                foreach (var hashes in category.Index.HashTableEntries.Values) {
                    if (hashes.IsSynonym)
                        continue;

                    var folderHash = unchecked((uint) (hashes.hash >> 32));
                    var folderEntry = hashDatabase.GetFolderEntry(indexId, folderHash);

                    VirtualFolder virtualFolder;

                    if (folderEntry is null) {
                        if (!unknownFolders.TryGetValue(Tuple.Create(category.Chunk, folderHash), out virtualFolder!)) {
                            unknownFolders.Add(Tuple.Create(category.Chunk, folderHash),
                                virtualFolder = new(category.Chunk, folderHash, currentFolder));
                        }
                    } else {
                        virtualFolder = folderEntry.Text == expectedPathPrefix
                            ? currentFolder
                            : currentFolder.GetOrCreateSubfolder(folderEntry.Text[(expectedPathPrefix.Length + 1)..]);                        
                    }

                    var fileHash = unchecked((uint) hashes.hash);
                    var fileEntry = folderEntry?.Files.FirstOrDefault(x => x.Hash == fileHash);
                    var virtualFile = new VirtualFile(
                        fileEntry?.Text ?? $"~{fileHash:X08}",
                        category,
                        hashes.DataFileId,
                        hashes.Offset);
                    virtualFolder.Files.Add(virtualFile);
                }

                using var fs = category.Index.File.OpenRead();
                using var br = new LuminaBinaryReader(fs, category.Index.SqPackHeader.platformId);
                br.Position = category.Index.IndexHeader.synonym_data_offset;
                foreach (var e in br.ReadStructuresAsArray<SqPackIndexFullPathEntry>(
                             (int) (category.Index.IndexHeader.synonym_data_size /
                                    Marshal.SizeOf<SqPackIndexFullPathEntry>()))) {
                    string name;
                    unsafe {
                        var len = 0;
                        while (e.Name[len] != 0)
                            len++;
                        name = Encoding.UTF8.GetString(e.Name, len);
                    }

                    // Marks end of struct list
                    if (string.IsNullOrWhiteSpace(name))
                        break;

                    var sep = name.LastIndexOf('/');
                    var folderName = sep == -1 ? "" : name[..sep];
                    var fileName = sep == -1 ? name : name[(sep + 1)..];

                    var virtualFolder = folderName == expectedPathPrefix
                        ? currentFolder
                        : currentFolder.GetOrCreateSubfolder(folderName[(expectedPathPrefix.Length + 1)..]);
                    var virtualFile = new VirtualFile(fileName, category, e.DataFileId, e.Offset);
                    virtualFolder.Files.Add(virtualFile);
                }
            }

            if (unknownFolders.Any()) {
                currentFolder.Folders.Add("<unknown>", new("<unknown>", currentFolder) {
                    _resolver = vf => {
                        foreach (var v in unknownFolders.Values)
                            vf.Folders.Add(v.Name, v);
                    }
                });
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct SqPackIndexFullPathEntry {
        public uint NameHash;
        public uint PathHash;
        public uint Data;
        public uint ConflictIndex;
        public fixed byte Name[0xF0];

        public byte DataFileId => (byte) ((Data & 0b1110) >> 1);

        public long Offset => (Data & ~0xF) * 0x08;
    }
}
