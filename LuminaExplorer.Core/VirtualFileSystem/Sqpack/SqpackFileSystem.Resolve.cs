using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Misc;
using LuminaExplorer.Core.SqPackPath;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem {
    private readonly Dictionary<SqpackFolder, Lazy<Task<IVirtualFolder>>> _childFoldersResolvers = new();
    private readonly Dictionary<SqpackFolder, Task<IVirtualFolder>> _childFilesResolvers = new();

    public bool IsFoldersResolved(IVirtualFolder ifolder) {
        var folder = (SqpackFolder) ifolder;
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

    public Task<IVirtualFolder> AsFoldersResolved(params string[] pathComponents)
        => AsFoldersResolvedImpl(RootFolderTyped, NormalizePath(pathComponents).Split('/'), 0);

    private Task<IVirtualFolder> AsFoldersResolvedImpl(IVirtualFolder ifolder, string[] parts, int partIndex) {
        var folder = (SqpackFolder) ifolder;
        for (; partIndex < parts.Length; partIndex++) {
            var name = parts[partIndex] + "/";
            if (name == "./")
                continue;

            if (name == "../") {
                folder = folder.ParentTyped ?? folder;
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

    public Task<IVirtualFolder> AsFoldersResolved(IVirtualFolder ifolder) {
        var folder = (SqpackFolder) ifolder;
        lock (_childFoldersResolvers) {
            if (!_childFoldersResolvers.TryGetValue(folder, out var resolver))
                return Task.FromResult(ifolder);

            if (resolver.Value.IsCompleted)
                _childFoldersResolvers.Remove(folder);

            return resolver.Value;
        }
    }

    public Task<IVirtualFolder> AsFileNamesResolved(IVirtualFolder ifolder) {
        var folder = (SqpackFolder) ifolder;
        lock (_childFilesResolvers) {
            if (folder.FileNamesResolveAttempted) {
                _childFilesResolvers.Remove(folder);
                return Task.FromResult(ifolder);
            }

            if (_childFilesResolvers.TryGetValue(folder, out var resolver))
                return resolver;

            SqpackFile[]? filesToResolve = null;

            var folderResolver = AsFoldersResolved(folder);
            if (folderResolver.IsCompleted) {
                filesToResolve = folder.Files.Where(f => !f.NameResolveAttempted).ToArray();
                if (!filesToResolve.Any()) {
                    folder.FileNamesResolveAttempted = true;
                    return Task.FromResult(ifolder);
                }
            }

            resolver = folderResolver.ContinueWith(_ => {
                (filesToResolve ?? folder.Files.Where(f => !f.NameResolveAttempted))
                    .AsParallel()
                    .ForAll(f => f.TryResolve());
                folder.FileNamesResolveAttempted = true;
                return ifolder;
            }, TaskContinuationOptions.RunContinuationsAsynchronously);

            _childFilesResolvers.Add(folder, resolver);

            return resolver;
        }
    }

    private void PopulateFolderResolverFor(
        SqpackFolder currentFolder,
        HashDatabase hashDatabase,
        string expectedPathPrefix,
        List<Category> chunks) {
        lock (_childFoldersResolvers) {
            _childFoldersResolvers.Add(currentFolder, new(() => Task.Run(() => {
                _treeStructureLock.EnterReadLock();
                var unknownContainer = SqpackFolder.CreateUnknownContainer(currentFolder);
                var unknownFolders = new Dictionary<Tuple<int, uint>, SqpackFolder>();
                try {
                    foreach (var category in chunks) {
                        var indexId = (uint) ((category.CategoryId << 16) | (category.Expansion << 8) | category.Chunk);

                        foreach (var hashes in category.Index.HashTableEntries.Values) {
                            if (hashes.IsSynonym)
                                continue;

                            var folderHash = unchecked((uint) (hashes.hash >> 32));
                            var folderEntry = hashDatabase.GetFolderEntry(indexId, folderHash);

                            SqpackFolder sqpackFolder;

                            if (folderEntry is null) {
                                if (!unknownFolders.TryGetValue(Tuple.Create(category.Chunk, folderHash),
                                        out sqpackFolder!)) {
                                    unknownFolders.Add(
                                        Tuple.Create(category.Chunk, folderHash),
                                        sqpackFolder = SqpackFolder.CreateUnknownEntry(
                                            category.Chunk,
                                            folderHash,
                                            unknownContainer));
                                }
                            } else {
                                var folderName = hashDatabase.GetString(folderEntry.Value.NameOffset);
                                sqpackFolder = folderName == expectedPathPrefix
                                    ? currentFolder
                                    : UnsafeGetOrCreateSubfolder(
                                        currentFolder,
                                        folderName[(expectedPathPrefix.Length + 1)..]);
                            }

                            var fileHash = unchecked((uint) hashes.hash);
                            var virtualFile = new SqpackFile(
                                folderEntry is null
                                    ? () => hashDatabase.FindFileName(indexId, fileHash)
                                    : () => hashDatabase.GetFileName(folderEntry.Value, fileHash),
                                indexId,
                                fileHash,
                                hashes.data,
                                sqpackFolder);
                            sqpackFolder.Files.Add(virtualFile);
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
                                : UnsafeGetOrCreateSubfolder(currentFolder,
                                    folderName[(expectedPathPrefix.Length + 1)..]);
                            var virtualFile = new SqpackFile(fileName, indexId, e.NameHash, e.Data, virtualFolder);
                            virtualFolder.Files.Add(virtualFile);
                        }
                    }

                    if (unknownFolders.Any()) {
                        _childFoldersResolvers.Add(unknownContainer, new(() => Task.Run(() => {
                            foreach (var v in unknownFolders.Values)
                                unknownContainer.Folders.Add(v.Name, v);
                            return (IVirtualFolder) unknownContainer;
                        })));
                        currentFolder.Folders.Add(unknownContainer.Name, unknownContainer);
                    }
                } finally {
                    _treeStructureLock.ExitReadLock();
                }

                return (IVirtualFolder) currentFolder;
            })));
        }
    }

    private SqpackFolder UnsafeGetOrCreateSubfolder(SqpackFolder parent, string path) {
        var sepOffset = path.IndexOf('/');
        var name = sepOffset == -1 ? path : path[..sepOffset];
        if (!parent.Folders.TryGetValue(name, out var subfolder)) {
            parent.Folders.Add(name, subfolder = SqpackFolder.CreateKnownEntry(
                name,
                UnsafeGetFullPath(parent) + name,
                parent));
        }

        if (sepOffset != -1)
            subfolder = UnsafeGetOrCreateSubfolder(subfolder, path[(sepOffset + 1)..]);
        return subfolder;
    }

    public void SuggestFullPath(string name) =>
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

            var chunkRootSolver = new HashSet<Task<IVirtualFolder>> {Task.FromResult(RootFolder)};
            var changedCallbacks = new List<Action>();
            while (chunkRootSolver.Any()) {
                var folderTask = await Task.WhenAny(chunkRootSolver).ConfigureAwait(false);
                chunkRootSolver.Remove(folderTask);

                var ifolder = await folderTask.ConfigureAwait(false);
                var folder = (SqpackFolder) ifolder;

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
                                .Where(x => !x.NameResolved && x.NameHash == nameHash)
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
                                .Where(x => x.Value.PathHash == folderNameHash)
                                .ToArray();

                            if (resolvingItems.Any()) {
                                _treeStructureLock.EnterWriteLock();
                                try {
                                    foreach (var (_, f) in resolvingItems) {
                                        var previousTree = UnsafeGetTreeFromRoot(f);

                                        // Might have been already processed in another WriteLocked section
                                        if (!f.IsUnknownFolder)
                                            continue;

                                        Debug.Assert(f.ParentTyped?.IsUnknownContainer is true,
                                            "I should be in an <unknown> folder.");
                                        Debug.Assert(f.ParentTyped.ParentTyped is not null,
                                            "<unknown> folder must have a parent.");

                                        var chunkRootName = UnsafeGetFullPath(f.ParentTyped.ParentTyped);
                                        var subPath = folderName[chunkRootName.Length..^1];
                                        // TODO: test this ^
                                        Debugger.Break();
                                        var tempDir = UnsafeGetOrCreateSubfolder(f, subPath);
                                        Debug.Assert(!tempDir.Files.Any(),
                                            "Temporary folder must be empty.");
                                        Debug.Assert(!tempDir.Folders.Any(),
                                            "Temporary folder must be empty.");
                                        var parent = tempDir.ParentTyped;
                                        Debug.Assert(parent is not null,
                                            "Temporary folder must have a parent.");
                                        parent.Folders.Remove(tempDir.Name);
                                        parent.Folders.Add(tempDir.Name, f);
                                        f.ParentTyped.Folders.Remove(f.Name);
                                        f.Name = tempDir.Name;
                                        f.ParentTyped = tempDir.ParentTyped;

                                        changedCallbacks.Add(() => FolderChanged?.Invoke(
                                            f, previousTree.Cast<IVirtualFolder>().ToArray()));
                                    }
                                } finally {
                                    _treeStructureLock.ExitWriteLock();
                                }
                            }
                        }

                        foreach (var (_, f) in folder.Folders) {
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
