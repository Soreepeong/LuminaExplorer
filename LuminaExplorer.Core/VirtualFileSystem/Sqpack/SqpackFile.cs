using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public class SqpackFile : IEquatable<SqpackFile>, IVirtualFile {
    internal Lazy<string?> LazyName;

    private readonly uint _dataFileIdAndOffset;

    internal readonly uint IndexId;

    internal SqpackFile(
        Func<string?> nameResolver,
        uint indexId,
        uint nameHash,
        uint dataFileIdAndOffset,
        SqpackFolder parent)
    {
        this.IndexId = indexId;
        this.NameHash = nameHash;
        this._dataFileIdAndOffset = dataFileIdAndOffset;
        this.ParentTyped = parent;
        this.LazyName = new(nameResolver);
    }

    internal SqpackFile(string name, uint indexId, uint nameHash, uint dataFileIdAndOffset, SqpackFolder parent)
    {
        this.IndexId = indexId;
        this.NameHash = nameHash;
        this._dataFileIdAndOffset = dataFileIdAndOffset;
        this.ParentTyped = parent;
        this.LazyName = new(name);
    }

    internal byte DataFileId => unchecked((byte) ((this._dataFileIdAndOffset & 0b1110) >> 1));

    internal long Offset => (this._dataFileIdAndOffset & ~0xF) << 3;

    public SqpackFolder ParentTyped { get; }

    public IVirtualFolder Parent => this.ParentTyped;

    public uint? NameHash { get; }

    internal void TryResolve() => _ = this.LazyName.Value;

    public string Name => this.LazyName.Value ?? $"~{this.NameHash:X08}";

    public bool Equals(SqpackFile? other) =>
        this._dataFileIdAndOffset == other?._dataFileIdAndOffset && this.IndexId == other.IndexId;

    public bool Equals(IVirtualFile? other) => this.Equals(other as SqpackFile);

    public override bool Equals(object? obj) => this.Equals(obj as SqpackFile);

    public override int GetHashCode() => (int) (this._dataFileIdAndOffset ^ this.IndexId);

    public override string ToString() => this.Name;

    public bool NameResolveAttempted => this.LazyName.IsValueCreated;

    public bool NameResolved => this.LazyName is { IsValueCreated: true, Value: not null };
}
