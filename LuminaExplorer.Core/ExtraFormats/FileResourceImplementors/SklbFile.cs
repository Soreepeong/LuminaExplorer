using System;
using System.Collections.Generic;
using System.IO;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".sklb")]
public class SklbFile : FileResource {
    public const uint MagicValue = 0x736B6C62;
    public const uint AlphMagicValue = 0x616C7068;

    public uint Magic;
    public SklbFormat Version;
    public ISklbVersionedHeader VersionedHeader = null!;
    public uint AlphMagic;
    public ushort[][] AlphData = null!;
    public byte[] HavokData = null!;

    public Node HavokRootNode = null!;
    public readonly Dictionary<Tuple<string, int>, Definition> HavokDefinitions = new();

    public override void LoadFile() {
        Reader.ReadInto(out Magic);
        Reader.ReadInto(out Version);

        VersionedHeader = Version switch {
            SklbFormat.K0021 => Reader.ReadStructure<Sklb0021>(),
            SklbFormat.K0031 => Reader.ReadStructure<Sklb0031>(),
            _ => throw new NotSupportedException()
        };

        AlphMagic = Reader.WithSeek(VersionedHeader.AlphOffset).ReadUInt32();
        if (AlphMagic == AlphMagicValue) {
            var bart = Reader.ReadStructuresAsArray<ushort>((VersionedHeader.HavokOffset - VersionedHeader.AlphOffset - 4) / 2);
            var tmp = new List<ushort[]>();
            for (var i = 0; i < bart.Length;) {
                var count = bart[i++];
                tmp.Add(bart[i..(i + count)]);
                i += count;
            }

            AlphData = tmp.ToArray();
        }
        
        HavokData = Data[VersionedHeader.HavokOffset..];

        try {
            HavokRootNode = Parser.Parse(HavokData, HavokDefinitions);
        } catch (Exception) {
            // pass
        }
    }

    public enum SklbFormat : uint {
        K0021 = 0x31323030u,
        K0031 = 0x31333030u,
    }

    public interface ISklbVersionedHeader {
        public int AlphOffset { get; }
        public int HavokOffset { get; }
    }

    public struct SklbVersionedHeaderCommon00210031 {
        public ushort ModelId;
        public SkeletonTargetModelClassification ModelClassification;
        public uint Unknown1;
        public uint Unknown2;
        public uint Unknown3;
        public uint Unknown4;
        public uint Unknown5;
        public uint Unknown6;
    }

    public struct Sklb0021 : ISklbVersionedHeader {
        public int AlphOffset {
            get => AlphOffsetU16;
            set => AlphOffsetU16 = checked((ushort) value);
        }

        public int HavokOffset {
            get => HavokOffsetU16;
            set => HavokOffsetU16 = checked((ushort) value);
        }

        public SkeletonTargetModelClassification ModelClassification {
            get => Common00210031.ModelClassification;
            set => Common00210031.ModelClassification = value;
        }

        public int ModelId {
            get => Common00210031.ModelId;
            set => Common00210031.ModelId = checked((ushort) value);
        }

        public ushort AlphOffsetU16;
        public ushort HavokOffsetU16;
        public SklbVersionedHeaderCommon00210031 Common00210031;
    }

    public struct Sklb0031 : ISklbVersionedHeader {
        public int AlphOffset {
            get => (int) AlphOffsetU32;
            set => AlphOffsetU32 = unchecked((uint) value);
        }

        public int HavokOffset {
            get => (int) HavokOffsetU32;
            set => HavokOffsetU32 = unchecked((uint) value);
        }

        public SkeletonTargetModelClassification ModelClassification {
            get => Common00210031.ModelClassification;
            set => Common00210031.ModelClassification = value;
        }

        public int ModelId {
            get => Common00210031.ModelId;
            set => Common00210031.ModelId = checked((ushort) value);
        }

        public uint AlphOffsetU32;
        public uint HavokOffsetU32;
        public uint Unknown1;
        public SklbVersionedHeaderCommon00210031 Common00210031;
    }
}
