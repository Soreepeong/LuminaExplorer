using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Lumina.Data;
using Lumina.Misc;
using LuminaExplorer.Core.SqPackPath;

namespace LuminaExplorer.Core.LazySqPackTree; 

public sealed partial class VirtualSqPackTree {
    private readonly Dictionary<VirtualFolder, Lazy<Task<VirtualFolder>>> _childFoldersResolvers = new();
    private readonly Dictionary<VirtualFolder, Task<VirtualFolder>> _childFilesResolvers = new();
    
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
            }, default,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
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

    public Task<VirtualFolder> AsFileNamesResolved(VirtualFolder folder) {
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

    private void PopulateFolderResolverFor(
        VirtualFolder currentFolder,
        HashDatabase hashDatabase,
        string expectedPathPrefix,
        List<Category> chunks) {
        lock (_childFoldersResolvers) {
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
}
