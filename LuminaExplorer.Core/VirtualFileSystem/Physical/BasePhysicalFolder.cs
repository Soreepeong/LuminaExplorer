using System;
using System.Collections.Generic;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public abstract class BasePhysicalFolder : IVirtualFolder {
    internal Lazy<List<PhysicalFolder>> Folders;
    internal Lazy<List<PhysicalFile>> Files;

    protected BasePhysicalFolder()
    {
        this.Folders = null!;
        this.Files = null!;
        this.Refresh();
    }

    public Exception? AccessException { get; private set; }
    public abstract bool Equals(IVirtualFolder? other);
    public abstract IVirtualFolder? Parent { get; }
    public uint? PathHash => null;
    public abstract string Name { get; }

    public void Refresh()
    {
        this.Folders = new(
            () => {
                try {
                    this.AccessException = null;
                    return this.ResolveFolders();
                } catch (Exception e) {
                    this.AccessException = e;
                    return new();
                }
            });
        this.Files = new(
            () => {
                try {
                    this.AccessException = null;
                    return this.ResolveFiles();
                } catch (Exception e) {
                    this.AccessException = e;
                    return new();
                }
            });
    }

    protected abstract List<PhysicalFolder> ResolveFolders();
    protected abstract List<PhysicalFile> ResolveFiles();
}
