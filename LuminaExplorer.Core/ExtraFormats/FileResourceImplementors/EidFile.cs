using System.IO;
using System.Linq;
using System.Numerics;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors; 

[FileExtension(".eid")]
public class EidFile : FileResource {
    public EidHeader Header;
    public EidBindPoint[] BindPoints = null!;
    
    public override void LoadFile() {
        Header = new(Reader);
        if (Header.Magic != EidHeader.MagicValue)
            throw new InvalidDataException();
        if (Header.Version != EidHeader.VersionValue)
            throw new InvalidDataException();
        BindPoints = Enumerable.Range(0, Header.Count).Select(_ => new EidBindPoint(Reader)).ToArray();
    }

    public struct EidHeader {
        public const uint MagicValue = 0x00656964;
        public const uint VersionValue = 0x31303132;
        
        public uint Magic = 0;
        public uint Version = 0;
        public int Count = 0;
        public uint Padding = 0;

        public EidHeader() { }

        public EidHeader(BinaryReader r) {
            r.ReadInto(out Magic);
            r.ReadInto(out Version);
            r.ReadInto(out Count);
            r.ReadInto(out Padding);
        }
    }

    public struct EidBindPoint {
        public string Name = string.Empty;
        public uint Id = 0;
        public Vector3 Position = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Zero;

        public EidBindPoint() { }

        public EidBindPoint(BinaryReader r) {
            Name = r.ReadFString(32);
            r.ReadInto(out Id);
            r.ReadInto(out Position);
            r.ReadInto(out Rotation);
        }
    }
}
