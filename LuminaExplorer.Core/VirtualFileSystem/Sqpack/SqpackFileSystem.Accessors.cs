using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem {
    private static string UnsafeGetFullPath(IVirtualFolder folder) =>
        folder.Parent is { } parent ? UnsafeGetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(IVirtualFolder folder)
    {
        this._treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(folder);
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    private static string UnsafeGetFullPath(IVirtualFile file) => UnsafeGetFullPath(file.Parent) + file.Name;

    public string GetFullPath(IVirtualFile file)
    {
        this._treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(file);
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public uint? GetFullPathHash(IVirtualFile file) => Crc32.Get(this.GetFullPath(file).Trim('/').ToLowerInvariant());

    private static SqpackFolder[] UnsafeGetTreeFromRoot(SqpackFolder folder)
    {
        var res = new List<SqpackFolder> { folder };
        while (res[^1].ParentTyped is { } parent)
            res.Add(parent);
        return Enumerable.Reverse(res).ToArray();
    }

    public SqpackFolder[] GetTreeFromRoot(SqpackFolder folder)
    {
        this._treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetTreeFromRoot(folder);
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public IVirtualFolder[] GetTreeFromRoot(IVirtualFolder folder)
    {
        this._treeStructureLock.EnterReadLock();
        try {
            var res = new List<IVirtualFolder> { folder };
            while (res[^1].Parent is { } parent)
                res.Add(parent);
            return Enumerable.Reverse(res).ToArray();
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public bool HasNoSubfolder(IVirtualFolder ifolder)
    {
        var folder = (SqpackFolder) ifolder;
        return (!folder.Folders.Any() || folder.Folders.All(x => Equals(x.Value, folder.ParentTyped)))
            && this.IsFoldersResolved(folder);
    }

    public int GetKnownFolderCount(IVirtualFolder ifolder)
    {
        var folder = (SqpackFolder) ifolder;
        if (!this.IsFoldersResolved(folder))
            throw new InvalidOperationException();

        this._treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Count(x => !x.Value.IsUnknownFolder);
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public Task<IVirtualFolder?> LocateFolder(IVirtualFolder root, params string[] pathComponents) => Task.Run(
        async () => {
            pathComponents = this.NormalizePath(pathComponents).Split('/');
            var folder = root;

            foreach (var pathComponent in pathComponents) {
                if (pathComponent == ".")
                    continue;
                if (pathComponent == "..") {
                    folder = folder.Equals(root) ? root : (folder.Parent ?? root);
                    continue;
                }

                var folders = this.GetFolders(await this.AsFoldersResolved(folder));
                folder = folders.FirstOrDefault(
                    x =>
                        string.Compare(x.Name, pathComponent + "/", StringComparison.InvariantCultureIgnoreCase) == 0);
                if (folder is null)
                    return null;
            }

            return folder;
        });

    public Task<IVirtualFile?> LocateFile(IVirtualFolder root, params string[] pathComponents) => Task.Run(
        async () => {
            pathComponents = this.NormalizePath(pathComponents).Split('/');
            var folder = await this.LocateFolder(root, pathComponents.SkipLast(1).ToArray());
            if (folder is null)
                return null;

            var files = this.GetFiles(await this.AsFoldersResolved(folder));

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

            if (!this.AreFileNamesResolved(folder))
                files = this.GetFiles(await this.AsFileNamesResolved(folder));

            return files.FirstOrDefault(
                x =>
                    string.Compare(x.Name, pathComponents.Last(), StringComparison.InvariantCultureIgnoreCase) == 0);
        });

    public List<SqpackFile> GetFiles(SqpackFolder folder)
    {
        if (!this.IsFoldersResolved(folder))
            throw new InvalidOperationException();

        this._treeStructureLock.EnterReadLock();
        try {
            return [..folder.Files];
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public List<IVirtualFile> GetFiles(IVirtualFolder folder)
    {
        var vfolder = (SqpackFolder) folder;
        if (!this.IsFoldersResolved(vfolder))
            throw new InvalidOperationException();

        this._treeStructureLock.EnterReadLock();
        try {
            return [..vfolder.Files];
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public List<SqpackFolder> GetFolders(SqpackFolder folder)
    {
        if (!this.IsFoldersResolved(folder))
            throw new InvalidOperationException();

        this._treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Select(x => x.Value).ToList();
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }

    public List<IVirtualFolder> GetFolders(IVirtualFolder folder)
    {
        var vfolder = (SqpackFolder) folder;
        if (!this.IsFoldersResolved(vfolder))
            throw new InvalidOperationException();

        this._treeStructureLock.EnterReadLock();
        try {
            return [..vfolder.Folders.Values];
        } finally {
            this._treeStructureLock.ExitReadLock();
        }
    }
}
