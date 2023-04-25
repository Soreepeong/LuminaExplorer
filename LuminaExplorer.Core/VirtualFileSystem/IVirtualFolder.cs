namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFolder {
    public const string UpFolderKey = "../";
    
    public IVirtualFolder? Parent { get; }

    public uint PathHash { get; }

    public string Name { get; }
}