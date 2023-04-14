namespace LuminaExplorer.LazySqPackTree;

public class VirtualFile {
    private readonly Lazy<string?> _name;
    private readonly uint _dataFileIdAndOffset;
    public readonly uint IndexId;
    public readonly uint FileHash;
    
    public byte DataFileId => unchecked((byte) ((_dataFileIdAndOffset & 0b1110) >> 1));
    public long Offset => (_dataFileIdAndOffset & ~0xF) << 3;

    internal VirtualFile(Func<string?> nameResolver, uint indexId, uint fileHash, uint dataFileIdAndOffset) {
        IndexId = indexId;
        FileHash = fileHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        _name = new(nameResolver);
    }

    internal VirtualFile(string name, uint indexId, uint fileHash, uint dataFileIdAndOffset) {
        IndexId = indexId;
        FileHash = fileHash;
        _dataFileIdAndOffset = dataFileIdAndOffset;
        _name = new(name);
    }

    public string Name => _name.Value ?? $"~{FileHash:X08}";

    // public SqPackFileInfo Metadata => _metadata ??= Owner.DatFiles[DataFileId].GetFileMetadata(Offset);
    //
    // public T GetFileTyped<T>() where T : FileResource => Owner.DatFiles[DataFileId].ReadFile<T>(Offset);
    //
    // public FileResource GetFile() => Owner.DatFiles[DataFileId].ReadFile<FileResource>(Offset);

    internal void TryResolve() => _ = _name.Value;

    public bool NameResolved => _name.IsValueCreated;
}
