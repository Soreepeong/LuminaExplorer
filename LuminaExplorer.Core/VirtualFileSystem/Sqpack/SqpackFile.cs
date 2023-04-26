using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public class SqpackFile : IEquatable<SqpackFile>, IVirtualFile {
    internal Lazy<string?> LazyName;
    
    private readonly uint _dataFileIdAndOffset;

    internal readonly uint IndexId;

    internal SqpackFile(Func<string?> nameResolver, uint indexId, uint nameHash, uint dataFileIdAndOffset, SqpackFolder parent) {
        IndexId = indexId;
        NameHash = nameHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        ParentTyped = parent;
        LazyName = new(nameResolver);
    }

    internal SqpackFile(string name, uint indexId, uint nameHash, uint dataFileIdAndOffset, SqpackFolder parent) {
        IndexId = indexId;
        NameHash = nameHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        ParentTyped = parent;
        LazyName = new(name);
    }
    
    internal byte DataFileId => unchecked((byte) ((_dataFileIdAndOffset & 0b1110) >> 1));
    
    internal long Offset => (_dataFileIdAndOffset & ~0xF) << 3;

    public SqpackFolder ParentTyped { get; }

    public IVirtualFolder Parent => ParentTyped;
    
    public uint? NameHash { get; }
    
    internal void TryResolve() => _ = LazyName.Value;

    public string Name => LazyName.Value ?? $"~{NameHash:X08}";

    public bool Equals(SqpackFile? other) =>
        _dataFileIdAndOffset == other?._dataFileIdAndOffset && IndexId == other.IndexId;

    public bool Equals(IVirtualFile? other) => Equals(other as SqpackFile);

    public override bool Equals(object? obj) => Equals(obj as SqpackFile);

    public override int GetHashCode() => (int)(_dataFileIdAndOffset ^ IndexId);

    public override string ToString() => Name;

    public bool NameResolveAttempted => LazyName.IsValueCreated;

    public bool NameResolved => LazyName is {IsValueCreated: true, Value: not null};
}
