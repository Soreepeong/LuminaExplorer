using Lumina.Misc;

namespace LuminaExplorer.Core.LazySqPackTree;

public class VirtualFile {
    private Lazy<string?> _name;
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
        _name = new(nameResolver);
    }

    internal VirtualFile(string name, uint indexId, uint fileHash, uint dataFileIdAndOffset, VirtualFolder parent) {
        IndexId = indexId;
        FileHash = fileHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        Parent = parent;
        _name = new(name);
    }

    internal bool TryResolve(string name) {
        if (Crc32.Get(name.ToLowerInvariant()) != FileHash)
            return false;
        
        _name = new(name);
        return true;
    }

    internal void TryResolve() => _ = _name.Value;

    public string Name => _name.Value ?? $"~{FileHash:X08}";

    public string FullPath => $"{Parent.FullPath}{Name}";

    public bool NameResolveAttempted => _name.IsValueCreated;

    public bool NameResolved => _name is {IsValueCreated: true, Value: not null};
}
