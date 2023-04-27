using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public sealed class ShellFileSystem : IVirtualFileSystem {
    private readonly IShellFolder _rootInterface;

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder ppshf);

    public ShellFileSystem() {
        Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out _rootInterface));
        RootFolderTyped = new(
            new(0),
            "",
            ShellItemFlags.Browsable | ShellItemFlags.Folder | ShellItemFlags.HasSubfolder,
            null);
    }

    public void Dispose() {
        Marshal.ReleaseComObject(_rootInterface);
    }

    public event IVirtualFileSystem.FolderChangedDelegate? FolderChanged;
    public event IVirtualFileSystem.FileChangedDelegate? FileChanged;
    public IVirtualFolder RootFolder => RootFolderTyped;
    public ShellFolder RootFolderTyped { get; }

    public IVirtualFileLookup GetLookup(IVirtualFile file) {
        throw new NotImplementedException();
    }

    public async Task<IVirtualFolder> AsFoldersResolved(params string[] pathComponents) {
        var folder = RootFolderTyped;
        foreach (var part in NormalizePath(pathComponents).Split('/')) {
            var name = part + "/";
            if (name == "./")
                continue;

            if (name == "../") {
                folder = folder.ParentTyped ?? folder;
                continue;
            }

            var subfolder = (await GetFoldersAsync(folder)).FirstOrDefault(
                f => string.Compare(f.Name, name, StringComparison.InvariantCultureIgnoreCase) == 0);
            if (subfolder is null)
                break;

            folder = subfolder;
        }

        return await AsFoldersResolved(folder);
    }

    public Task<IVirtualFolder> AsFoldersResolved(IVirtualFolder folder) => Task.WhenAll(
            GetFoldersAsync((ShellFolder) folder),
            GetFilesAsync((ShellFolder) folder))
        .ContinueWith(_ => folder);

    public Task<IVirtualFolder> AsFileNamesResolved(IVirtualFolder folder) => AsFoldersResolved(folder);

    public void SuggestFullPath(string name) { }

    public string NormalizePath(params string[] pathComponents) =>
        Path.Join(pathComponents).Replace('\\', '/').Trim('/');

    public string GetFullPath(IVirtualFolder folder) =>
        folder.Parent is { } parent ? GetFullPath(parent) + folder.Name : folder.Name;

    public string GetFullPath(IVirtualFile file) => GetFullPath(file.Parent) + file.Name;

    public uint? GetFullPathHash(IVirtualFile file) =>null;

    public IVirtualFolder[] GetTreeFromRoot(IVirtualFolder folder) {
        var res = new List<IVirtualFolder> {folder};
        while (res[^1].Parent is { } parent)
            res.Add(parent);
        return Enumerable.Reverse(res).ToArray();
    }

    public bool HasNoSubfolder(IVirtualFolder folder) =>
        !((ShellFolder) folder).Flags.HasFlag(ShellItemFlags.HasSubfolder);

    public int GetKnownFolderCount(IVirtualFolder folder) {
        var task = GetFoldersAsync((ShellFolder) folder);
        if (!task.IsCompletedSuccessfully)
            throw task.Exception ?? throw new InvalidOperationException();
        return task.Result.Count;
    }

    public List<IVirtualFile> GetFiles(IVirtualFolder folder) {
        var task = GetFilesAsync((ShellFolder) folder);
        if (!task.IsCompletedSuccessfully)
            throw task.Exception ?? throw new InvalidOperationException();
        return task.Result.Cast<IVirtualFile>().ToList();
    }

    public List<IVirtualFolder> GetFolders(IVirtualFolder folder) {
        var task = GetFoldersAsync((ShellFolder) folder);
        if (!task.IsCompletedSuccessfully)
            throw task.Exception ?? throw new InvalidOperationException();
        return task.Result.Cast<IVirtualFolder>().ToList();
    }

    public unsafe Task<List<ShellFile>> GetFilesAsync(ShellFolder folder) {
        return folder.FilesTask ??= Task.Run(() => {
            const EnumObjectFlags enumFlags =
                EnumObjectFlags.NonFolders |
                EnumObjectFlags.IncludeHidden |
                EnumObjectFlags.IncludeSuperHidden;
            var res = new List<ShellFile>();
            IEnumIDList? enumId = null;
            IShellFolder? obj = null;
            var idl1 = ArrayPool<nint>.Shared.Rent(1);
            try {
                obj = folder.Idl.Data.Any() ? BindToObject<IShellFolder>(folder.Idl) : _rootInterface;
                obj.EnumObjects(0, enumFlags, out enumId);
                var items = new nint[64];
                while (true) {
                    enumId.Next(items.Length, items, out var ritems);

                    if (ritems == 0)
                        break;

                    foreach (var idl in items[..ritems].Select(x => new ShellItemId(x)).ToArray()) {
                        var flags =
                            ShellItemFlags.Folder |
                            ShellItemFlags.HasSubfolder |
                            ShellItemFlags.Browsable |
                            ShellItemFlags.Stream;

                        string name;
                        fixed (void* pidl = idl.Data) {
                            idl1[0] = (nint) pidl;
                            obj.GetAttributesOf(1, idl1, ref flags);
                            obj.GetDisplayNameOf(idl1[0], NameFlags.Normal, out var ret);
                            name = ret.ToStringAndDispose();
                        }

                        res.Add(new(idl, name, flags, folder));
                    }
                }
            } catch (Exception e) {
                folder.AccessException = e;
            } finally {
                ArrayPool<nint>.Shared.Return(idl1);
                if (obj is not null && obj != _rootInterface)
                    Marshal.ReleaseComObject(obj);
                if (enumId is not null)
                    Marshal.ReleaseComObject(enumId);
            }

            return res;
        });
    }

    public unsafe Task<List<ShellFolder>> GetFoldersAsync(ShellFolder folder) {
        return folder.FoldersTask ??= Task.Run(() => {
            const EnumObjectFlags enumFlags =
                EnumObjectFlags.Folders |
                EnumObjectFlags.IncludeHidden |
                EnumObjectFlags.IncludeSuperHidden;
            var res = new List<ShellFolder>();
            IEnumIDList? enumId = null;
            IShellFolder? obj = null;
            var idl1 = ArrayPool<nint>.Shared.Rent(1);
            try {
                obj = folder.Idl.Data.Any() ? BindToObject<IShellFolder>(folder.Idl) : _rootInterface;
                obj.EnumObjects(0, enumFlags, out enumId);
                var items = new nint[64];
                while (true) {
                    enumId.Next(items.Length, items, out var ritems);

                    if (ritems == 0)
                        break;

                    foreach (var idl in items[..ritems].Select(x => new ShellItemId(x)).ToArray()) {
                        var flags =
                            ShellItemFlags.Folder |
                            ShellItemFlags.HasSubfolder |
                            ShellItemFlags.Browsable |
                            ShellItemFlags.Stream;

                        string name;
                        fixed (void* pidl = idl.Data) {
                            idl1[0] = (nint) pidl;
                            obj.GetAttributesOf(1, idl1, ref flags);
                            obj.GetDisplayNameOf(idl1[0], NameFlags.Normal, out var ret);
                            name = ret.ToStringAndDispose() + "/";
                        }

                        res.Add(new(idl, name, flags, folder));
                    }
                }
            } catch (Exception e) {
                folder.AccessException = e;
            } finally {
                ArrayPool<nint>.Shared.Return(idl1);
                if (obj is not null && obj != _rootInterface)
                    Marshal.ReleaseComObject(obj);
                if (enumId is not null)
                    Marshal.ReleaseComObject(enumId);
            }

            return res;
        });
    }

    public unsafe T BindToObject<T>(ShellFolder folder, ShellItemId idl) {
        fixed (void* pidl = idl.Data) {
            var guid = typeof(T).GUID;
            _rootInterface.BindToObject(
                (nint) pidl,
                0,
                ref guid,
                out var ppv);
            try {
                var obj = (T) Marshal.GetObjectForIUnknown(ppv);
                return obj;
            } finally {
                Marshal.Release(ppv);
            }
        }
    }

    public unsafe T BindToStorage<T>(ShellItemId idl) {
        fixed (void* pidl = idl.Data) {
            var guid = typeof(T).GUID;
            _rootInterface.BindToStorage(
                (nint) pidl,
                0,
                ref guid,
                out var ppv);
            try {
                var obj = (T) Marshal.GetObjectForIUnknown(ppv);
                return obj;
            } finally {
                Marshal.Release(ppv);
            }
        }
    }
}