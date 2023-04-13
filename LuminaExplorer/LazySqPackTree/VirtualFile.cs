using Lumina.Data;

namespace LuminaExplorer.LazySqPackTree;

public class VirtualFile {
    public readonly string Name;
    public readonly Category Owner;
    public readonly byte DataFileId;
    public readonly long Offset;

    public VirtualFile(string name, Category owner, byte dataFileId, long offset) {
        Name = name;
        Owner = owner;
        DataFileId = dataFileId;
        Offset = offset;
    }
}