using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public sealed partial class SqpackFileSystem {
    private static string UnsafeGetFullPath(VirtualFolder folder) =>
        folder.ParentTyped is { } parent ? UnsafeGetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(IVirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath((VirtualFolder) folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    private static string UnsafeGetFullPath(VirtualFile file) => UnsafeGetFullPath(file.ParentTyped) + file.Name;

    public string GetFullPath(IVirtualFile file) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath((VirtualFile) file);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public uint GetFullPathHash(IVirtualFile file) => Crc32.Get(GetFullPath(file).Trim('/').ToLowerInvariant());

    private static VirtualFolder[] UnsafeGetTreeFromRoot(VirtualFolder folder) {
        var res = new List<VirtualFolder> {folder};
        while (res[^1].ParentTyped is { } parent)
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
        var folder = (VirtualFolder) ifolder;
        return (!folder.Folders.Any() || folder.Folders.All(x => x.Value == folder.ParentTyped))
               && IsFoldersResolved(folder);
    }

    public int GetKnownFolderCount(IVirtualFolder ifolder) {
        var folder = (VirtualFolder) ifolder;
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();
        
        _treeStructureLock.EnterReadLock();
        try {
            return folder.Folders.Count(x => !x.Value.IsUnknownFolder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    } 

    public List<VirtualFile> GetFiles(VirtualFolder folder) {
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
        var vfolder = (VirtualFolder) folder;
        if (!IsFoldersResolved(vfolder))
            throw new InvalidOperationException();

        _treeStructureLock.EnterReadLock();
        try {
            return new(vfolder.Files);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public List<VirtualFolder> GetFolders(VirtualFolder folder, bool excludeUpDir = true) {
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();
        
        _treeStructureLock.EnterReadLock();
        try {
            return excludeUpDir
                ? folder.Folders.Where(x => x.Key != IVirtualFolder.UpFolderKey).Select(x => x.Value).ToList()
                : folder.Folders.Values.ToList();
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public List<IVirtualFolder> GetFolders(IVirtualFolder folder, bool excludeUpDir = true) {
        var vfolder = (VirtualFolder) folder;
        if (!IsFoldersResolved(vfolder))
            throw new InvalidOperationException();
        
        _treeStructureLock.EnterReadLock();
        try {
            return new(excludeUpDir
                ? vfolder.Folders.Where(x => x.Key != IVirtualFolder.UpFolderKey).Select(x => x.Value)
                : vfolder.Folders.Values);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }
}
