namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public class ShellFile : ShellItem, IVirtualFile {
    public ShellFile(ShellItemId idl, string name, ShellItemFlags flags, ShellFolder? parent) : base(idl, name, flags,
        parent) { }

    public IVirtualFolder Parent => ParentTyped!;

    public uint? NameHash => null;

    public bool NameResolved => true;

    public bool Equals(IVirtualFile? other) => base.Equals(other as ShellItem);
}