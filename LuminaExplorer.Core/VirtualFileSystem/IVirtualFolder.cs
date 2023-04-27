using System;

namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFolder : IEquatable<IVirtualFolder> {
    public Exception? AccessException { get; }
    
    public IVirtualFolder? Parent { get; }

    public uint? PathHash { get; }

    public string Name { get; }
}