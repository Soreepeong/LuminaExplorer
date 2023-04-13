using Lumina.Data;
using Lumina.Data.Structs;

namespace LuminaExplorer.LazySqPackTree;

public class VirtualFile {
    private string? _name;
    private Func<string?>? _nameResolver;
    private SqPackFileInfo? _metadata;
    
    public readonly Category Owner;
    public readonly uint IndexId;
    public readonly uint FileHash;
    public readonly byte DataFileId;
    public readonly long Offset;

    public VirtualFile(Func<string?> nameResolver, uint indexId, uint fileHash, Category owner, byte dataFileId, long offset) {
        _nameResolver = nameResolver;
        Owner = owner;
        IndexId = indexId;
        FileHash = fileHash;
        DataFileId = dataFileId;
        Offset = offset;
    }

    public VirtualFile(string name, uint indexId, uint fileHash, Category owner, byte dataFileId, long offset) {
        _name = name;
        Owner = owner;
        IndexId = indexId;
        FileHash = fileHash;
        DataFileId = dataFileId;
        Offset = offset;
    }

    internal void TryResolve() {
        if (_nameResolver is null)
            return;
        var resolved = _nameResolver();
        if (resolved is not null)
            _name = resolved;
        _nameResolver = null;
    }

    public string Name => _name ?? $"{FileHash:X08}";

    public SqPackFileInfo Metadata => _metadata ??= Owner.DatFiles[DataFileId].GetFileMetadata(Offset);

    public bool NameResolved => _nameResolver is null;
}