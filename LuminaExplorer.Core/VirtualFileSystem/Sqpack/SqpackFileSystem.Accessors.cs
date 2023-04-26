using System;
using System.Collections.Generic;
using System.Linq;
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
