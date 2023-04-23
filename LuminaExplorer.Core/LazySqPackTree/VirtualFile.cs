using System;

namespace LuminaExplorer.Core.LazySqPackTree;

public class VirtualFile {
    internal Lazy<string?> LazyName;
    private readonly uint _dataFileIdAndOffset;

    public readonly VirtualFolder Parent;
    public readonly uint IndexId;
    public readonly uint FileHash;
    
    public byte DataFileId => unchecked((byte) ((_dataFileIdAndOffset & 0b1110) >> 1));
    public long Offset => (_dataFileIdAndOffset & ~0xF) << 3;

    internal VirtualFile(Func<string?> nameResolver, uint indexId, uint fileHash, uint dataFileIdAndOffset, VirtualFolder parent) {
        IndexId = indexId;
        FileHash = fileHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        Parent = parent;
        LazyName = new(nameResolver);
    }

    internal VirtualFile(string name, uint indexId, uint fileHash, uint dataFileIdAndOffset, VirtualFolder parent) {
        IndexId = indexId;
        FileHash = fileHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        Parent = parent;
        LazyName = new(name);
    }

    internal void TryResolve() => _ = LazyName.Value;

    public string Name => LazyName.Value ?? $"~{FileHash:X08}";

    public override string ToString() => Name;

    public bool NameResolveAttempted => LazyName.IsValueCreated;

    public bool NameResolved => LazyName is {IsValueCreated: true, Value: not null};
}
