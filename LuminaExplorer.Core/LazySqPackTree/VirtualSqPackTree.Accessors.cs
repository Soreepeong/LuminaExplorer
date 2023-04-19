using Lumina.Misc;

namespace LuminaExplorer.Core.LazySqPackTree;

public sealed partial class VirtualSqPackTree {
    private static string UnsafeGetFullPath(VirtualFolder folder) =>
        folder.Parent is { } parent ? UnsafeGetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(VirtualFolder folder) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(folder);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    private static string UnsafeGetFullPath(VirtualFile file) => UnsafeGetFullPath(file.Parent) + file.Name;

    public string GetFullPath(VirtualFile file) {
        _treeStructureLock.EnterReadLock();
        try {
            return UnsafeGetFullPath(file);
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }

    public uint GetFullPathHash(VirtualFile file) => Crc32.Get(GetFullPath(file).Trim('/').ToLowerInvariant());

    private static VirtualFolder[] UnsafeGetTreeFromRoot(VirtualFolder folder) {
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

    public bool WillFolderNeverHaveSubfolders(VirtualFolder folder) =>
        folder.Folders.All(x => x.Value == folder.Parent) && IsFoldersResolved(folder);

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

    public List<VirtualFolder> GetFolders(VirtualFolder folder, bool excludeUpDir = true) {
        if (!IsFoldersResolved(folder))
            throw new InvalidOperationException();
        
        _treeStructureLock.EnterReadLock();
        try {
            return excludeUpDir
                ? folder.Folders.Where(x => x.Key != VirtualFolder.UpFolderKey).Select(x => x.Value).ToList()
                : folder.Folders.Values.ToList();
        } finally {
            _treeStructureLock.ExitReadLock();
        }
    }
}
