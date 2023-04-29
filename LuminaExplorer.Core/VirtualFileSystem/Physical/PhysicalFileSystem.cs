using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public class PhysicalFileSystem : IVirtualFileSystem {
    public void Dispose() { }

    public event IVirtualFileSystem.FolderChangedDelegate? FolderChanged;

    public event IVirtualFileSystem.FileChangedDelegate? FileChanged;

    public IVirtualFolder RootFolder => MyComputerFolder.Instance;

    public IVirtualFileLookup GetLookup(IVirtualFile file) => file is PhysicalFile pf
        ? new PhysicalFileLookup(pf)
        : throw new ArgumentException("Only PhysicalFile is accepted", nameof(file));

    public Task<IVirtualFolder> AsFoldersResolved(params string[] pathComponents) {
        var folder = (BasePhysicalFolder) RootFolder;
        foreach (var part in NormalizePath(pathComponents).Split('/')) {
            var name = part + "/";
            if (name == "./")
                continue;

            if (name == "../") {
                folder = folder.Parent as PhysicalFolder ?? folder;
                continue;
            }

            var subfolder = folder.Folders.Value.FirstOrDefault(
                f => string.Compare(f.Name, name, StringComparison.InvariantCultureIgnoreCase) == 0);
            if (subfolder is null)
                break;

            folder = subfolder;
        }

        return AsFoldersResolved(folder);
    }

    public Task<IVirtualFolder> AsFoldersResolved(IVirtualFolder folder) {
        var f = (BasePhysicalFolder) folder;
        if (f.Folders.IsValueCreated)
            return Task.FromResult(folder);

        return Task.Run(() => {
            _ = f.Folders.Value;
            return folder;
        });
    }

    public Task<IVirtualFolder> AsFileNamesResolved(IVirtualFolder folder) {
        var f = (BasePhysicalFolder) folder;
        if (f.Files.IsValueCreated)
            return Task.FromResult(folder);

        return Task.Run(() => {
            _ = f.Files.Value;
            return folder;
        });
    }

    public bool AreFileNamesResolved(IVirtualFolder folder) => true;

    public void SuggestFullPath(string name) { }

    public string NormalizePath(params string[] pathComponents) =>
        Path.Join(pathComponents).Replace('\\', '/').Trim('/');

    public string GetFullPath(IVirtualFolder folder) =>
        folder.Parent is { } parent ? GetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(IVirtualFile file) => GetFullPath(file.Parent) + file.Name;

    public uint? GetFullPathHash(IVirtualFile file) => null;

    public IVirtualFolder[] GetTreeFromRoot(IVirtualFolder folder) {
        var res = new List<IVirtualFolder> {folder};
        while (res[^1].Parent is { } parent)
            res.Add(parent);
        return Enumerable.Reverse(res).ToArray();
    }

    public bool HasNoSubfolder(IVirtualFolder folder) => !(folder as BasePhysicalFolder)!.Folders.Value.Any();

    public int GetKnownFolderCount(IVirtualFolder folder) => (folder as BasePhysicalFolder)!.Folders.Value.Count;

    public Task<IVirtualFile?> FindFile(IVirtualFolder root, params string[] pathComponents) {
        return Task.Run(async () => {
            var path = NormalizePath(pathComponents).Split('/');
            var folder = root;

            foreach (var pathComponent in path.SkipLast(1)) {
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

            return (IVirtualFile?) ((BasePhysicalFolder) folder).Files.Value
                .FirstOrDefault(x =>
                    string.Compare(x.Name, pathComponents.Last(), StringComparison.InvariantCultureIgnoreCase) == 0);
        });
    }

    public List<IVirtualFile> GetFiles(IVirtualFolder folder) =>
        ((BasePhysicalFolder) folder).Files.Value.Cast<IVirtualFile>().ToList();

    public List<IVirtualFolder> GetFolders(IVirtualFolder folder) =>
        ((BasePhysicalFolder) folder).Folders.Value.Cast<IVirtualFolder>().ToList();
}
