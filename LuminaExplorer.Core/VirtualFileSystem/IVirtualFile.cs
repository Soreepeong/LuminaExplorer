namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFile {
    public IVirtualFolder? Parent { get; }
    
    public uint NameHash { get; }

    public string Name { get; }

    public bool NameResolved { get; }
}