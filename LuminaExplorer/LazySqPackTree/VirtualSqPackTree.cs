using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.SqPackPath;
using LuminaExplorer.Util;

namespace LuminaExplorer.LazySqPackTree;

public class VirtualSqPackTree {
    public readonly DirectoryInfo InstallationSqPackDirectory;
    public readonly VirtualFolder RootFolder = new("", null);
    public readonly PlatformId PlatformId;

    private readonly Dictionary<VirtualFolder, Lazy<Task<VirtualFolder>>> _childFoldersResolvers = new();
    private readonly Dictionary<VirtualFolder, Task<VirtualFolder>> _childFilesResolvers = new();

    private readonly LruCache<VirtualFile, VirtualFileLookup> _fileLookups = new(128);

    public VirtualSqPackTree(HashDatabase hashDatabase, GameData gameData) {
        InstallationSqPackDirectory = gameData.DataPath;
        PlatformId = gameData.Options.CurrentPlatform;
        
        _childFoldersResolvers.Add(RootFolder, new(() => Task.Run(() => {
            foreach (var (categoryId, categoryName) in Repository.CategoryIdToNameMap) {
                var repos = gameData.Repositories
                    .Where(x => x.Value.Categories.GetValueOrDefault(categoryId)?.Count is > 0)
                    .ToDictionary(x => x.Key, x => x.Value.Categories[categoryId]);
                switch (repos.Count) {
                    case 1:
                        PopulateFolderResolverFor(RootFolder.GetOrCreateSubfolder(categoryName), hashDatabase,
                            categoryName, repos.First().Value);
                        break;

                    case > 1: {
                        var categoryNode = RootFolder.GetOrCreateSubfolder(categoryName);
                        foreach (var (repoName, chunks) in repos) {
                            PopulateFolderResolverFor(categoryNode.GetOrCreateSubfolder(repoName), hashDatabase,
                                $"{categoryName}/{repoName}", chunks);
                        }

                        break;
                    }
                }
            }

            return RootFolder;
        })));
    }

    public VirtualFileLookup GetLookup(VirtualFile file) {
        if (_fileLookups.TryGet(file, out var data))
            return data;

        var cat = unchecked((byte) (file.IndexId >> 16));
        var ex = unchecked((byte) (file.IndexId >> 8));
        var chunk = unchecked((byte) file.IndexId);
        var repoName = (file.IndexId & 0x00FF00) == 0
            ? "ffxiv"
            : $"ex{(file.IndexId >> 8) & 0xFF:D}";
        var fileName = Repository.BuildDatStr(cat, ex, chunk, PlatformId, $"dat{file.DataFileId}");
        var datPath = Path.Combine(InstallationSqPackDirectory.FullName, repoName, fileName);

        data = new(file, PlatformId, File.OpenRead(datPath));
        
        _fileLookups.Add(file, data);
        return data;
    }

    public bool WillFolderNeverHaveSubfolders(VirtualFolder folder) =>
        !folder.Folders.Any() && IsFoldersResolved(folder);

    public bool IsFoldersResolved(VirtualFolder folder) {
        lock (_childFoldersResolvers) {
            if (!_childFoldersResolvers.TryGetValue(folder, out var resolver))
                return true;

            if (resolver.Value.IsCompleted)
                _childFoldersResolvers.Remove(folder);

            return resolver.Value.IsCompleted;
        }
    }

    public Task<VirtualFolder> AsFoldersResolved(VirtualFolder folder) {
        lock (_childFoldersResolvers) {
            if (!_childFoldersResolvers.TryGetValue(folder, out var resolver))
                return Task.FromResult(folder);

            if (resolver.Value.IsCompleted)
                _childFoldersResolvers.Remove(folder);

            return resolver.Value;
        }
    }

    public Task<VirtualFolder> AsFilesResolved(VirtualFolder folder) {
        lock (_childFilesResolvers) {
            if (folder.FileNamesResolveAttempted) {
                _childFilesResolvers.Remove(folder);
                return Task.FromResult(folder);
            }

            if (_childFilesResolvers.TryGetValue(folder, out var resolver))
                return resolver;

            VirtualFile[]? filesToResolve = null;
            
            var folderResolver = AsFoldersResolved(folder);
            if (folderResolver.IsCompleted) {
                filesToResolve = folder.Files.Where(f => !f.NameResolved).ToArray();
                if (!filesToResolve.Any()) {
                    folder.FileNamesResolveAttempted = true;
                    return Task.FromResult(folder);
                }
            }

            resolver = folderResolver.ContinueWith(_ => {
                (filesToResolve ?? folder.Files.Where(f => !f.NameResolved))
                    .AsParallel()
                    .ForAll(f => f.TryResolve());
                folder.FileNamesResolveAttempted = true;
                return folder;
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            _childFilesResolvers.Add(folder, resolver);

            return resolver;
        }
    }

    private void PopulateFolderResolverFor(
        VirtualFolder currentFolder,
        HashDatabase hashDatabase,
        string expectedPathPrefix,
        List<Category> chunks) {
        _childFoldersResolvers.Add(currentFolder, new(() => Task.Run(() => {
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
                            unknownFolders.Add(
                                Tuple.Create(category.Chunk, folderHash),
                                virtualFolder = new(category.Chunk, folderHash, currentFolder));
                        }
                    } else {
                        var folderName = hashDatabase.GetString(folderEntry.Value.NameOffset);
                        virtualFolder = folderName == expectedPathPrefix
                            ? currentFolder
                            : currentFolder.GetOrCreateSubfolder(folderName[(expectedPathPrefix.Length + 1)..]);
                    }

                    var fileHash = unchecked((uint) hashes.hash);
                    var virtualFile = new VirtualFile(
                        folderEntry is null
                            ? () => hashDatabase.FindFileName(indexId, fileHash)
                            : () => hashDatabase.GetFileName(folderEntry.Value, fileHash),
                        indexId,
                        fileHash,
                        hashes.data);
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
                    var virtualFile = new VirtualFile(fileName, indexId, e.NameHash, e.Data);
                    virtualFolder.Files.Add(virtualFile);
                }
            }

            if (unknownFolders.Any()) {
                var unknownFolder = new VirtualFolder("<unknown>", currentFolder);
                _childFoldersResolvers.Add(unknownFolder, new(() => Task.Run(() => {
                    foreach (var v in unknownFolders.Values)
                        unknownFolder.Folders.Add(v.Name, v);
                    return unknownFolder;
                })));
                currentFolder.Folders.Add("<unknown>", unknownFolder);
            }

            return currentFolder;
        })));
    }

    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
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
