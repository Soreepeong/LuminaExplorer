using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;
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
    public Bone[] Bones = null!;

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
            
            /*
             * root.namedVariants[0].variant.skeletons[0]
             *     .name => who cares
             *     .parentIndices[N] => int (-1 = root)
             *     .bones[N].name => str
             *     .referencePose[n] => float4x3 (TRS)
             */
            var resultBones = new List<Bone>();
            if (HavokRootNode.AsMap.GetValueOrDefault("namedVariants") is not ValueArray namedVariants)
                throw new(); // care later about errmsg
            if (namedVariants.Values.FirstOrDefault() is not ValueNode namedVariant0)
                throw new();
            if (namedVariant0.Node.AsMap.GetValueOrDefault("variant") is not ValueNode variant)
                throw new();
            if (variant.Node.AsMap.GetValueOrDefault("skeletons") is not ValueArray skeletons)
                throw new();
            if (skeletons.Values.FirstOrDefault() is not ValueNode skeleton)
                throw new();
            if (skeleton.Node.AsMap.GetValueOrDefault("parentIndices") is not ValueArray parentIndices)
                throw new();
            if (skeleton.Node.AsMap.GetValueOrDefault("bones") is not ValueArray bones)
                throw new();
            if (skeleton.Node.AsMap.GetValueOrDefault("referencePose") is not ValueArray referencePoses)
                throw new();
            foreach (var (boneValue, parentIndexValue, referencePoseValue) in bones.Values.Zip(
                         parentIndices.Values, referencePoses.Values)) {
                var bone = new Bone {
                    Index = resultBones.Count
                };

                if (boneValue is not ValueNode boneNode)
                    throw new();
                if (boneNode.Node.AsMap.GetValueOrDefault("name") is not ValueString name)
                    throw new();
                bone.Name = name.Value;
                
                if (parentIndexValue is not ValueInt parentIndex)
                    throw new();
                bone.ParentIndex = parentIndex.Value;
                
                if (referencePoseValue is not ValueArray poseFloats)
                    throw new();
                bone.Translation.X = poseFloats.Values[0] is ValueFloat tx ? tx.Value : throw new();
                bone.Translation.Y = poseFloats.Values[1] is ValueFloat ty ? ty.Value : throw new();
                bone.Translation.Z = poseFloats.Values[2] is ValueFloat tz ? tz.Value : throw new();
                bone.Rotation.X = poseFloats.Values[4] is ValueFloat rx ? rx.Value : throw new();
                bone.Rotation.Y = poseFloats.Values[5] is ValueFloat ry ? ry.Value : throw new();
                bone.Rotation.Z = poseFloats.Values[6] is ValueFloat rz ? rz.Value : throw new();
                bone.Rotation.W = poseFloats.Values[7] is ValueFloat rw ? rw.Value : throw new();
                bone.Scale.X = poseFloats.Values[8] is ValueFloat sx ? sx.Value : throw new();
                bone.Scale.Y = poseFloats.Values[9] is ValueFloat sy ? sy.Value : throw new();
                bone.Scale.Z = poseFloats.Values[10] is ValueFloat sz ? sz.Value : throw new();

                resultBones.Add(bone);
            }

            Bones = resultBones.ToArray();

        } catch (Exception) {
            // pass
        }
    }

    public struct Bone {
        public int Index;
        public int ParentIndex;
        public string Name;
        public Vector3 Translation;
        public Quaternion Rotation;
        public Vector3 Scale;

        public Matrix4x4 Matrix => 
            Matrix4x4.CreateScale(Scale) *
            Matrix4x4.CreateFromQuaternion(Rotation) *
            Matrix4x4.CreateTranslation(Translation);
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
