using Be.Windows.Forms;
using Lumina.Data;

namespace LuminaExplorer.App.Utils;

public class FileResourceByteProvider : IByteProvider {
    private readonly FileResource _fileResource;

    public FileResourceByteProvider(FileResource fileResource) {
        _fileResource = fileResource;
    }

    public byte ReadByte(long index) => _fileResource.Data[index];

    public void WriteByte(long index, byte value) => throw new NotSupportedException();

    public void InsertBytes(long index, byte[] bs) => throw new NotSupportedException();

    public void DeleteBytes(long index, long length) => throw new NotSupportedException();

    public long Length => _fileResource.Data.LongLength;

    public event EventHandler? LengthChanged;

    public bool HasChanges() => false;

    public void ApplyChanges() => throw new NotSupportedException();

    public event EventHandler? Changed;

    public bool SupportsWriteByte() => false;

    public bool SupportsInsertBytes() => false;

    public bool SupportsDeleteBytes() => false;
}
