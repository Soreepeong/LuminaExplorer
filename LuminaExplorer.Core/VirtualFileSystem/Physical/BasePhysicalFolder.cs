using System;
using System.Collections.Generic;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public abstract class BasePhysicalFolder : IVirtualFolder {
    internal Lazy<List<PhysicalFolder>> Folders;
    internal Lazy<List<PhysicalFile>> Files;

    protected BasePhysicalFolder() {
        Folders = null!;
        Files = null!;
        Refresh();
    }

    public Exception? AccessException { get; private set; }
    public abstract bool Equals(IVirtualFolder? other);
    public abstract IVirtualFolder? Parent { get; }
    public uint? PathHash => null;
    public abstract string Name { get; }

    public void Refresh() {
        Folders = new(() => {
            try {
                AccessException = null;
                return ResolveFolders();
            } catch (Exception e) {
                AccessException = e;
                return new();
            }
        });
        Files = new(() => {
            try {
                AccessException = null;
                return ResolveFiles();
            } catch (Exception e) {
                AccessException = e;
                return new();
            }
        });
    }

    protected abstract List<PhysicalFolder> ResolveFolders();
    protected abstract List<PhysicalFile> ResolveFiles();
}
