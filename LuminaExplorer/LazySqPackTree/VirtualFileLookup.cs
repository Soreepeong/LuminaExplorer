using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data.Structs;
using LuminaExplorer.LazySqPackTree.VirtualFileStream;

namespace LuminaExplorer.LazySqPackTree; 

public sealed class VirtualFileLookup : IDisposable {
    public readonly BaseVirtualFileStream DataStream;
    public readonly FileType Type;
    public readonly uint Size;
    public readonly uint ReservedSpaceUnits;
    public readonly uint OccupiedSpaceUnits;

    public unsafe VirtualFileLookup(VirtualFile virtualFile, Stream datStream) {
        datStream.Position = virtualFile.Offset;
        
        var mdlBlock = new ModelBlock();
        var mdlBlockReadSize = datStream.Read(new(&mdlBlock, Marshal.SizeOf<ModelBlock>()));
        if (mdlBlockReadSize < Marshal.SizeOf<SqPackFileInfo>()) {
            datStream.Close();
            throw new InvalidDataException();
        }

        Type = mdlBlock.Type;
        Size = mdlBlock.RawFileSize;
        ReservedSpaceUnits = mdlBlock.NumberOfBlocks;
        OccupiedSpaceUnits = mdlBlock.UsedNumberOfBlocks;

        switch (Type) {
            case FileType.Empty: {
                DataStream = new EmptyVirtualFileStream(ReservedSpaceUnits, OccupiedSpaceUnits);
                break;
            }
            case FileType.Standard: {
                DataStream = new StandardVirtualFileStream(datStream, virtualFile.Offset, mdlBlock.Version, Size,
                    ReservedSpaceUnits, OccupiedSpaceUnits);
                break;
            }
            case FileType.Model: {
                DataStream = new ModelVirtualFileStream(datStream, virtualFile.Offset, Size,
                    ReservedSpaceUnits, OccupiedSpaceUnits);
                break;
            }
            case FileType.Texture: {
                break;
            }
            default:
                throw new NotSupportedException();
        }
    }

    public void Dispose() {
        DataStream.Dispose();
    }
}
