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

    public override void LoadFile()
    {
        this.Header = new(this.Reader);
        if (this.Header.Magic != EidHeader.MagicValue)
            throw new InvalidDataException();
        if (this.Header.Version != EidHeader.VersionValue)
            throw new InvalidDataException();
        this.BindPoints = Enumerable.Range(0, this.Header.Count).Select(_ => new EidBindPoint(this.Reader)).ToArray();
    }

    public struct EidHeader {
        public const uint MagicValue = 0x00656964;
        public const uint VersionValue = 0x31303132;

        public uint Magic = 0;
        public uint Version = 0;
        public int Count = 0;
        public uint Padding = 0;

        public EidHeader()
        { }

        public EidHeader(BinaryReader r)
        {
            r.ReadInto(out this.Magic);
            r.ReadInto(out this.Version);
            r.ReadInto(out this.Count);
            r.ReadInto(out this.Padding);
        }
    }

    public struct EidBindPoint {
        public string Name = string.Empty;
        public uint Id = 0;
        public Vector3 Position = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Zero;

        public EidBindPoint()
        { }

        public EidBindPoint(BinaryReader r)
        {
            this.Name = r.ReadFString(32);
            r.ReadInto(out this.Id);
            r.ReadInto(out this.Position);
            r.ReadInto(out this.Rotation);
        }
    }
}
