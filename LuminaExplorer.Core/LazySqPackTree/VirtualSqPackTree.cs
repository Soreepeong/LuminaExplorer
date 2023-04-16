using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using Lumina.Misc;
using LuminaExplorer.Core.SqPackPath;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed class VirtualSqPackTree : IDisposable {
    // Enter writer lock when nodes may be moved around across parents not in same hierarchy.
    private readonly ReaderWriterLockSlim _treeStructureLock = new();

    private readonly Dictionary<VirtualFolder, Lazy<Task<VirtualFolder>>> _childFoldersResolvers = new();
    private readonly Dictionary<VirtualFolder, Task<VirtualFolder>> _childFilesResolvers = new();
    private readonly LruCache<VirtualFile, VirtualFileLookup> _fileLookups = new(128);

    public readonly DirectoryInfo InstallationSqPackDirectory;
    public readonly VirtualFolder RootFolder = VirtualFolder.CreateRoot();
    public readonly PlatformId PlatformId;

    public event FolderChangedDelegate? FolderChanged;
    public event FileChangedDelegate? FileChanged;

    public VirtualSqPackTree(HashDatabase hashDatabase, GameData gameData) {
        InstallationSqPackDirectory = gameData.DataPath;
        PlatformId = gameData.Options.CurrentPlatform;

        _childFoldersResolvers.Add(RootFolder, new(() => Task.Run(() => {
            _treeStructureLock.EnterReadLock();
            try {
                foreach (var (categoryId, categoryName) in Repository.CategoryIdToNameMap) {
                    var repos = gameData.Repositories
                        .Where(x => x.Value.Categories.GetValueOrDefault(categoryId)?.Count is > 0)
                        .ToDictionary(x => x.Key, x => x.Value.Categories[categoryId]);
                    switch (repos.Count) {
                        case 1:
                            PopulateFolderResolverFor(
                                UnsafeGetOrCreateSubfolder(RootFolder, categoryName),
                                hashDatabase,
                                categoryName,
                                repos.First().Value);
                            break;

                        case > 1: {
                            var categoryNode = UnsafeGetOrCreateSubfolder(RootFolder, categoryName);
                            foreach (var (repoName, chunks) in repos) {
                                PopulateFolderResolverFor(
                                    UnsafeGetOrCreateSubfolder(categoryNode, repoName),
                                    hashDatabase,
                                    $"{categoryName}/{repoName}",
                                    chunks);
                            }

                            break;
                        }
                    }
                }
            } finally {
                _treeStructureLock.ExitReadLock();
            }

            return RootFolder;
        })));
    }

    public void Dispose() {
        _fileLookups.Dispose();
        FolderChanged = null;
        FileChanged = null;
    }

    public Task SuggestFullPath(string name) =>
        Task.Run(async () => {
            name = NormalizePath(name);
            var sep = name.LastIndexOf('/');
            if (sep == -1)
                return;

            var folderName = name[..sep];
            var folderNameHash = Crc32.Get(folderName.ToLowerInvariant());

            folderName = $"/{folderName}/";
            name = name[(sep + 1)..];
            var nameHash = Crc32.Get(name.ToLowerInvariant());

            var chunkRootSolver = new HashSet<Task<VirtualFolder>> {Task.FromResult(RootFolder)};
            var changedCallbacks = new List<Action>();
            while (chunkRootSolver.Any()) {
                var folderTask = await Task.WhenAny(chunkRootSolver).ConfigureAwait(false);
                chunkRootSolver.Remove(folderTask);

                var folder = await folderTask.ConfigureAwait(false);

                changedCallbacks.Clear();

                _treeStructureLock.EnterUpgradeableReadLock();

                // "Unsafe" to ensure no await is done inside. "Cannot 'await' in unsafe context."
                // ReSharper disable once RedundantUnsafeContext
                unsafe {
                    try {
                        if (0 == string.Compare(
                                UnsafeGetFullPath(folder),
                                folderName,
                                StringComparison.InvariantCultureIgnoreCase)) {
                            var resolvingItems = folder.Files
                                .Where(x => !x.NameResolved && x.FileHash == nameHash)
                                .ToArray();
                            if (resolvingItems.Any()) {
                                _treeStructureLock.EnterWriteLock();
                                try {
                                    foreach (var f in resolvingItems) {
                                        // Might have been already processed in another WriteLocked section
                                        if (f.Name == name)
                                            continue;

                                        f.LazyName = new(name);
                                        changedCallbacks.Add(() => FileChanged?.Invoke(f));
                                    }
                                } finally {
                                    _treeStructureLock.ExitWriteLock();
                                }
                            }
                        }

                        if (folder.Folders.Values.FirstOrDefault(x => x.IsUnknownContainer) is { } unknownFolder) {
                            // found an unknown folder; this folder is a sqpack chunk.
                            // if unknown folder exists, then it always is resolved.
                            var resolvingItems = unknownFolder.Folders
                                .Where(x => x.Key != VirtualFolder.UpFolderKey &&
                                            x.Value.FolderHash == folderNameHash)
                                .ToArray();

                            if (resolvingItems.Any()) {
                                _treeStructureLock.EnterWriteLock();
                                try {
                                    foreach (var (_, f) in resolvingItems) {
                                        var previousTree = UnsafeGetTreeFromRoot(f);

                                        // Might have been already processed in another WriteLocked section
                                        if (!f.IsUnknownFolder)
                                            continue;

                                        Debug.Assert(f.Parent?.IsUnknownContainer is true,
                                            "I should be in an <unknown> folder.");
                                        Debug.Assert(f.Parent.Parent is not null,
                                            "<unknown> folder must have a parent.");

                                        var chunkRootName = UnsafeGetFullPath(f.Parent.Parent);
                                        var subPath = folderName[chunkRootName.Length..^1];
                                        // TODO: test this ^
                                        Debugger.Break();
                                        var tempDir = UnsafeGetOrCreateSubfolder(f, subPath);
                                        Debug.Assert(!tempDir.Files.Any(),
                                            "Temporary folder must be empty.");
                                        Debug.Assert(tempDir.Folders.All(x => x.Key == VirtualFolder.UpFolderKey),
                                            "Temporary folder must be empty.");
                                        Debug.Assert(tempDir.Parent is not null,
                                            "Temporary folder must have a parent.");
                                        tempDir.Parent.Folders.Remove(tempDir.Name);
                                        tempDir.Parent.Folders.Add(tempDir.Name, f);
                                        f.Parent.Folders.Remove(f.Name);
                                        f.Name = tempDir.Name;
                                        f.Parent = tempDir.Parent;

                                        changedCallbacks.Add(() => FolderChanged?.Invoke(f, previousTree));
                                    }
                                } finally {
                                    _treeStructureLock.ExitWriteLock();
                                }
                            }
                        }

                        foreach (var (_, f) in folder.Folders.Where(x => x.Key != VirtualFolder.UpFolderKey)) {
                            if (folderName.StartsWith(UnsafeGetFullPath(f),
                                    StringComparison.InvariantCultureIgnoreCase))
                                chunkRootSolver.Add(AsFoldersResolved(f));
                        }
                    } finally {
                        _treeStructureLock.ExitUpgradeableReadLock();

                        changedCallbacks.AsParallel().ForAll(x => x());
                    }
                }
            }
        });

    private string UnsafeGetFullPath(VirtualFolder folder) =>
        folder.Parent is { } parent ? UnsafeGetFullPath(parent) + folder.Name : folder.Name;

    private string UnsafeGetFullPath(VirtualFile file) => UnsafeGetFullPath(file.Parent) + file.Name;

    public string GetFullPath(VirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public string GetFullPath(VirtualFile file) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(file);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    private VirtualFolder[] UnsafeGetTreeFromRoot(VirtualFolder folder) {
        var res = new List<VirtualFolder> {folder};
        while (res[^1].Parent is { } parent)
            res.Add(parent);
        return Enumerable.Reverse(res).ToArray();
    }

    public VirtualFolder[] GetTreeFromRoot(VirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetTreeFromRoot(folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public VirtualFileLookup GetLookup(VirtualFile file) {
        if (_fileLookups.TryGet(file, out var data))
            return (VirtualFileLookup) data.Clone();

        var cat = unchecked((byte) (file.IndexId >> 16));
        var ex = unchecked((byte) (file.IndexId >> 8));
        var chunk = unchecked((byte) file.IndexId);
        var repoName = (file.IndexId & 0x00FF00) == 0
            ? "ffxiv"
            : $"ex{(file.IndexId >> 8) & 0xFF:D}";
        var fileName = Repository.BuildDatStr(cat, ex, chunk, PlatformId, $"dat{file.DataFileId}");
        var datPath = Path.Combine(InstallationSqPackDirectory.FullName, repoName, fileName);

        data = new(this, file, new(File.OpenRead(datPath), PlatformId));

        _fileLookups.Add(file, data);
        return (VirtualFileLookup) data.Clone();
    }

    public bool WillFolderNeverHaveSubfolders(VirtualFolder folder) =>
        folder.Folders.All(x => x.Value == folder.Parent) && IsFoldersResolved(folder);

    public bool IsFoldersResolved(VirtualFolder folder) {
        lock (_childFoldersResolvers) {
            if (!_childFoldersResolvers.TryGetValue(folder, out var resolver))
                return true;

            if (!resolver.IsValueCreated)
                return false;

            if (resolver.Value.IsCompleted)
                _childFoldersResolvers.Remove(folder);

            return resolver.Value.IsCompleted;
        }
    }

    public Task<VirtualFolder> AsFoldersResolved(params string[] pathComponents)
        => AsFoldersResolvedImpl(RootFolder, NormalizePath(pathComponents).Split('/'), 0);

    public int GetKnownFolderCount(VirtualFolder folder) {
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();
        
        _treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Count(x => !x.Value.IsUnknownFolder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }
    
    public List<VirtualFolder> GetFolders(VirtualFolder folder, bool excludeUpDir = true) {
        _treeStructureLock.EnterReadLock();
        try {
            return excludeUpDir
                ? folder.Folders.Where(x => x.Key != VirtualFolder.UpFolderKey).Select(x => x.Value).ToList()
                : folder.Folders.Values.ToList();
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }
    
    public List<VirtualFile> GetFiles(VirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return new(folder.Files);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    private Task<VirtualFolder> AsFoldersResolvedImpl(VirtualFolder folder, string[] parts, int partIndex) {
        for (; partIndex < parts.Length; partIndex++) {
            var name = parts[partIndex] + "/";
            if (name == "./")
                continue;

            if (name == VirtualFolder.UpFolderKey) {
                folder = folder.Parent ?? folder;
                continue;
            }

            return AsFoldersResolved(folder).ContinueWith(_ => {
                var subfolder = folder.Folders.Values.FirstOrDefault(
                    f => string.Compare(f.Name, name, StringComparison.InvariantCultureIgnoreCase) == 0);
                return subfolder is null
                    ? AsFoldersResolved(folder)
                    : AsFoldersResolvedImpl(subfolder, parts, partIndex + 1);
            }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        return AsFoldersResolved(folder);
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
                filesToResolve = folder.Files.Where(f => !f.NameResolveAttempted).ToArray();
                if (!filesToResolve.Any()) {
                    folder.FileNamesResolveAttempted = true;
                    return Task.FromResult(folder);
                }
            }

            resolver = folderResolver.ContinueWith(_ => {
                (filesToResolve ?? folder.Files.Where(f => !f.NameResolveAttempted))
                    .AsParallel()
                    .ForAll(f => f.TryResolve());
                folder.FileNamesResolveAttempted = true;
                return folder;
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            _childFilesResolvers.Add(folder, resolver);

            return resolver;
        }
    }

    private VirtualFolder UnsafeGetOrCreateSubfolder(VirtualFolder parent, string path) {
        var sepOffset = path.IndexOf('/');
        var name = sepOffset == -1 ? path : path[..sepOffset];
        if (!parent.Folders.TryGetValue(name, out var subfolder)) {
            parent.Folders.Add(name, subfolder = VirtualFolder.CreateKnownEntry(
                name,
                UnsafeGetFullPath(parent) + name,
                parent));
        }

        if (sepOffset != -1)
            subfolder = UnsafeGetOrCreateSubfolder(subfolder, path[(sepOffset + 1)..]);
        return subfolder;
    }

    private void PopulateFolderResolverFor(
        VirtualFolder currentFolder,
        HashDatabase hashDatabase,
        string expectedPathPrefix,
        List<Category> chunks) {
        _childFoldersResolvers.Add(currentFolder, new(() => Task.Run(() => {
            _treeStructureLock.EnterReadLock();
            var unknownContainer = VirtualFolder.CreateUnknownContainer(currentFolder);
            var unknownFolders = new Dictionary<Tuple<int, uint>, VirtualFolder>();
            try {
                foreach (var category in chunks) {
                    var indexId = (uint) ((category.CategoryId << 16) | (category.Expansion << 8) | category.Chunk);

                    foreach (var hashes in category.Index.HashTableEntries.Values) {
                        if (hashes.IsSynonym)
                            continue;

                        var folderHash = unchecked((uint) (hashes.hash >> 32));
                        var folderEntry = hashDatabase.GetFolderEntry(indexId, folderHash);

                        VirtualFolder virtualFolder;

                        if (folderEntry is null) {
                            if (!unknownFolders.TryGetValue(Tuple.Create(category.Chunk, folderHash),
                                    out virtualFolder!)) {
                                unknownFolders.Add(
                                    Tuple.Create(category.Chunk, folderHash),
                                    virtualFolder = VirtualFolder.CreateUnknownEntry(
                                        category.Chunk,
                                        folderHash,
                                        unknownContainer));
                            }
                        } else {
                            var folderName = hashDatabase.GetString(folderEntry.Value.NameOffset);
                            virtualFolder = folderName == expectedPathPrefix
                                ? currentFolder
                                : UnsafeGetOrCreateSubfolder(
                                    currentFolder,
                                    folderName[(expectedPathPrefix.Length + 1)..]);
                        }

                        var fileHash = unchecked((uint) hashes.hash);
                        var virtualFile = new VirtualFile(
                            folderEntry is null
                                ? () => hashDatabase.FindFileName(indexId, fileHash)
                                : () => hashDatabase.GetFileName(folderEntry.Value, fileHash),
                            indexId,
                            fileHash,
                            hashes.data,
                            virtualFolder);
                        virtualFolder.Files.Add(virtualFile);
                    }

                    using var fs = category.Index.File.OpenRead();
                    using var br = new LuminaBinaryReader(fs, category.Index.SqPackHeader.platformId);
                    br.Position = category.Index.IndexHeader.synonym_data_offset;
                    foreach (var e in br.ReadStructuresAsArray<SqPackIndexFullPathEntry>(
                                 (int) (category.Index.IndexHeader.synonym_data_size /
                                        Unsafe.SizeOf<SqPackIndexFullPathEntry>()))) {
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
                            : UnsafeGetOrCreateSubfolder(currentFolder, folderName[(expectedPathPrefix.Length + 1)..]);
                        var virtualFile = new VirtualFile(fileName, indexId, e.NameHash, e.Data, virtualFolder);
                        virtualFolder.Files.Add(virtualFile);
                    }
                }

                if (unknownFolders.Any()) {
                    _childFoldersResolvers.Add(unknownContainer, new(() => Task.Run(() => {
                        foreach (var v in unknownFolders.Values)
                            unknownContainer.Folders.Add(v.Name, v);
                        return unknownContainer;
                    })));
                    currentFolder.Folders.Add(unknownContainer.Name, unknownContainer);
                }
            } finally {
                _treeStructureLock.ExitReadLock();
            }

            return currentFolder;
        })));
    }

    public static string NormalizePath(params string[] pathComponents) =>
        Path.Join(pathComponents).Replace('\\', '/').Trim('/');

    public delegate void FileChangedDelegate(VirtualFile changedFile);

    public delegate void FolderChangedDelegate(VirtualFolder changedFolder, VirtualFolder[]? previousPathFromRoot);

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
