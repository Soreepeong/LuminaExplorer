using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public class VirtualFile : IVirtualFile {
    internal Lazy<string?> LazyName;
    private readonly uint _dataFileIdAndOffset;

    public readonly uint IndexId;
    
    public byte DataFileId => unchecked((byte) ((_dataFileIdAndOffset & 0b1110) >> 1));
    public long Offset => (_dataFileIdAndOffset & ~0xF) << 3;

    internal VirtualFile(Func<string?> nameResolver, uint indexId, uint nameHash, uint dataFileIdAndOffset, VirtualFolder parent) {
        IndexId = indexId;
        NameHash = nameHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        ParentTyped = parent;
        LazyName = new(nameResolver);
    }

    internal VirtualFile(string name, uint indexId, uint nameHash, uint dataFileIdAndOffset, VirtualFolder parent) {
        IndexId = indexId;
        NameHash = nameHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        ParentTyped = parent;
        LazyName = new(name);
    }

    public VirtualFolder ParentTyped { get; }

    public IVirtualFolder Parent => ParentTyped;
    
    public uint NameHash { get; }
    
    internal void TryResolve() => _ = LazyName.Value;

    public string Name => LazyName.Value ?? $"~{NameHash:X08}";

    public override string ToString() => Name;

    public bool NameResolveAttempted => LazyName.IsValueCreated;

    public bool NameResolved => LazyName is {IsValueCreated: true, Value: not null};
}
