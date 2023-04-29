using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".pap")]
public class PapFile : FileResource {
    public PapHeader Header;
    public List<PapAnimation> Animations = null!;
    public byte[] HavokData = null!;
    public byte[] Timeline = null!;

    public Node HavokRootNode = null!;
    public readonly Dictionary<Tuple<string, int>, Definition> HavokDefinitions = new();

    public override void LoadFile() {
        Header = new(Reader);
        if (Header.Magic != PapHeader.MagicValue)
            throw new InvalidDataException();
        
        Reader.BaseStream.Position = Header.InfoOffset;
        Animations = Enumerable.Range(0, Header.AnimationCount).Select(_ => new PapAnimation(Reader)).ToList();

        HavokData = Data[Header.HavokDataOffset..Header.TimelineOffset];
        Timeline = Data[Header.TimelineOffset..];

        try {
            HavokRootNode = Parser.Parse(HavokData, HavokDefinitions);
        } catch (Exception) {
            // pass
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PapHeader {
        public const uint MagicValue = 0x20706170;
        
        public uint Magic;
        public uint Version;  // always 0x00020001?
        public short AnimationCount;
        public ushort ModelId;
        public SkeletonTargetModelClassification ModelClassification;
        public int InfoOffset;
        public int HavokDataOffset;
        public int TimelineOffset;

        public PapHeader(BinaryReader r) {
            r.ReadInto(out Magic);
            r.ReadInto(out Version);
            r.ReadInto(out AnimationCount);
            r.ReadInto(out ModelId);
            r.ReadInto(out ModelClassification);
            r.ReadInto(out InfoOffset);
            r.ReadInto(out HavokDataOffset);
            r.ReadInto(out TimelineOffset);
        }
    }

    public class PapAnimation {
        public string Name;
        public short Unknown20;
        public int Index;
        public short Unknown26;

        public PapAnimation(BinaryReader r) {
            var nameBytes = r.ReadBytes(0x20);
            Name = Encoding.UTF8.GetString(nameBytes, 0, nameBytes.TakeWhile(x => x != 0).Count());
            r.ReadInto(out Unknown20);
            r.ReadInto(out Index);
            r.ReadInto(out Unknown26);
        }
    }
}