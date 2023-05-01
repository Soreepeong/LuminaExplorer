using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem {
    private static string UnsafeGetFullPath(IVirtualFolder folder) =>
        folder.Parent is { } parent ? UnsafeGetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(IVirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    private static string UnsafeGetFullPath(IVirtualFile file) => UnsafeGetFullPath(file.Parent) + file.Name;

    public string GetFullPath(IVirtualFile file) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(file);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public uint? GetFullPathHash(IVirtualFile file) => Crc32.Get(GetFullPath(file).Trim('/').ToLowerInvariant());

    private static SqpackFolder[] UnsafeGetTreeFromRoot(SqpackFolder folder) {
        var res = new List<SqpackFolder> {folder};
        while (res[^1].ParentTyped is { } parent)
            res.Add(parent);
        return Enumerable.Reverse(res).ToArray();
    }

    public SqpackFolder[] GetTreeFromRoot(SqpackFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetTreeFromRoot(folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public IVirtualFolder[] GetTreeFromRoot(IVirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            var res = new List<IVirtualFolder> {folder};
            while (res[^1].Parent is { } parent)
                res.Add(parent);
            return Enumerable.Reverse(res).ToArray();
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public bool HasNoSubfolder(IVirtualFolder ifolder) {
        var folder = (SqpackFolder) ifolder;
        return (!folder.Folders.Any() || folder.Folders.All(x => Equals(x.Value, folder.ParentTyped)))
            && IsFoldersResolved(folder);
    }

    public int GetKnownFolderCount(IVirtualFolder ifolder) {
        var folder = (SqpackFolder) ifolder;
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Count(x => !x.Value.IsUnknownFolder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public Task<IVirtualFolder?> LocateFolder(IVirtualFolder root, params string[] pathComponents) => Task.Run(async () => {
        pathComponents = NormalizePath(pathComponents).Split('/');
        var folder = root;

        foreach (var pathComponent in pathComponents) {
            if (pathComponent == ".")
                continue;
            if (pathComponent == "..") {
                folder = folder.Equals(root) ? root : (folder.Parent ?? root);
                continue;
            }

            var folders = GetFolders(await AsFoldersResolved(folder));
            folder = folders.FirstOrDefault(x =>
                string.Compare(x.Name, pathComponent + "/", StringComparison.InvariantCultureIgnoreCase) == 0);
            if (folder is null)
                return null;
        }

        return folder;
    });

    public Task<IVirtualFile?> LocateFile(IVirtualFolder root, params string[] pathComponents) => Task.Run(async () => {
        pathComponents = NormalizePath(pathComponents).Split('/');
        var folder = await LocateFolder(root, pathComponents.SkipLast(1).ToArray());
        if (folder is null)
            return null;

        var files = GetFiles(await AsFoldersResolved(folder));

        // Do we have a matching name hash?
        var nameHash = Crc32.Get(pathComponents.Last().ToLowerInvariant());
        using (var fileEnumerator = files.Where(x => x.NameHash == nameHash).GetEnumerator()) {
            if (fileEnumerator.MoveNext()) {
                var file = fileEnumerator.Current;

                // Are there duplicates, and names must be checked?
                if (!fileEnumerator.MoveNext())
                    return file;
            }
        }

        if (!AreFileNamesResolved(folder))
            files = GetFiles(await AsFileNamesResolved(folder));

        return files.FirstOrDefault(x =>
            string.Compare(x.Name, pathComponents.Last(), StringComparison.InvariantCultureIgnoreCase) == 0);
    });

    public List<SqpackFile> GetFiles(SqpackFolder folder) {
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return new(folder.Files);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public List<IVirtualFile> GetFiles(IVirtualFolder folder) {
        var vfolder = (SqpackFolder) folder;
        if (!IsFoldersResolved(vfolder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return new(vfolder.Files);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public List<SqpackFolder> GetFolders(SqpackFolder folder) {
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Select(x => x.Value).ToList();
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public List<IVirtualFolder> GetFolders(IVirtualFolder folder) {
        var vfolder = (SqpackFolder) folder;
        if (!IsFoldersResolved(vfolder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return new(vfolder.Folders.Values);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }
}
