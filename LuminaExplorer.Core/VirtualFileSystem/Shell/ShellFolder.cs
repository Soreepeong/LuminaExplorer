using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public class ShellFolder : ShellItem, IVirtualFolder {
    internal Task<List<ShellFolder>>? FoldersTask;
    internal Task<List<ShellFile>>? FilesTask;

    public ShellFolder(ShellItemId idl, string name, ShellItemFlags flags, ShellFolder? parent) : base(idl, name, flags,
        parent) { }

    public Exception? AccessException { get; internal set; }

    public IVirtualFolder? Parent => ParentTyped;

    public uint? PathHash => null;

    public bool Equals(IVirtualFolder? other) => base.Equals(other as ShellItem);
}