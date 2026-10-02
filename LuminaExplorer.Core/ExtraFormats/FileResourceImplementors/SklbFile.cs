using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Extensions;
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
    public AlphEntry[] AlphData = [];
    public byte[] HavokData = null!;
    public Bone[] Bones = [];

    public Node HavokRootNode = null!;
    public readonly Dictionary<Tuple<string, int>, Definition> HavokDefinitions = new();

    public Exception? LoadException { get; private set; }

    public override void LoadFile()
    {
        try {
            this.Reader.ReadInto(out this.Magic);
            if (this.Magic != MagicValue)
                throw new InvalidDataException($"Invalid sklb magic 0x{this.Magic:X08}");
            this.Reader.ReadInto(out this.Version);

            // Versions up to "1200" use 16-bit offsets; later ones use 32-bit offsets.
            // In both cases, the havok data offset directly follows the alph (layer) offset.
            this.VersionedHeader = this.Version switch {
                SklbFormat.K0011 => this.Reader.ReadStructure<Sklb0021>(),
                SklbFormat.K0111 => this.Reader.ReadStructure<Sklb0021>(),
                SklbFormat.K0021 => this.Reader.ReadStructure<Sklb0021>(),
                SklbFormat.K0031 => this.Reader.ReadStructure<Sklb0031>(),
                SklbFormat.K1031 => this.Reader.ReadStructure<Sklb0031>(),
                _ => throw new NotSupportedException($"Unsupported sklb version 0x{(uint) this.Version:X08}"),
            };

            this.AlphMagic = this.Reader.WithSeek(this.VersionedHeader.AlphOffset).ReadUInt32();
            if (this.AlphMagic == AlphMagicValue) {
                var numOffsets = this.Reader.ReadUInt16();
                var offsets = this.Reader.ReadUInt16Array(numOffsets);
                this.AlphData = offsets
                    .Select(x => new AlphEntry(this.Reader.WithSeek(this.VersionedHeader.AlphOffset + x)))
                    .ToArray();
            }

            this.HavokData = this.Data[this.VersionedHeader.HavokOffset..];

            this.HavokRootNode = Parser.Parse(
                this.Reader.WithSeek(this.VersionedHeader.HavokOffset),
                this.HavokDefinitions);

            /*
             * root.namedVariants[0].variant.skeletons[0]
             *     .name => who cares
             *     .parentIndices[N] => int (-1 = root)
             *     .bones[N].name => str
             *     .referencePose[n] => float4x3 (TRS)
             */
            var resultBones = new List<Bone>();
            if (this.HavokRootNode.AsMap.GetValueOrDefault("namedVariants") is not ValueArray namedVariants)
                throw new(); // care later about errmsg
            if (namedVariants.Values
                    .FirstOrDefault(
                        x => x is ValueNode y &&
                            y.Node.AsMap.GetValueOrDefault("name") is ValueString { Value: "hkaAnimationContainer" })
                is not ValueNode namedVariant0)
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
                         parentIndices.Values,
                         referencePoses.Values)) {
                if (boneValue is not ValueNode boneNode)
                    throw new();
                if (boneNode.Node.AsMap.GetValueOrDefault("name") is not ValueString name)
                    throw new();
                if (parentIndexValue is not ValueInt parentIndex)
                    throw new();
                if (referencePoseValue is not ValueArray poseFloats)
                    throw new();

                resultBones.Add(
                    new(
                        resultBones.Count,
                        parentIndex.Value == -1 ? null : resultBones[parentIndex.Value],
                        name.Value,
                        new(
                            poseFloats.Values[0] is ValueFloat tx ? tx.Value : throw new(),
                            poseFloats.Values[1] is ValueFloat ty ? ty.Value : throw new(),
                            poseFloats.Values[2] is ValueFloat tz ? tz.Value : throw new()),
                        /* Discard poseFloats.Values[3] */
                        new(
                            poseFloats.Values[4] is ValueFloat rx ? rx.Value : throw new(),
                            poseFloats.Values[5] is ValueFloat ry ? ry.Value : throw new(),
                            poseFloats.Values[6] is ValueFloat rz ? rz.Value : throw new(),
                            poseFloats.Values[7] is ValueFloat rw ? rw.Value : throw new()),
                        new(
                            poseFloats.Values[8] is ValueFloat sx ? sx.Value : throw new(),
                            poseFloats.Values[9] is ValueFloat sy ? sy.Value : throw new(),
                            poseFloats.Values[10] is ValueFloat sz ? sz.Value : throw new())
                        /* Discard poseFloats.Values[11] */));
            }

            this.Bones = resultBones.ToArray();
            foreach (var ae in this.AlphData) {
                ae.Bones = new Bone[ae.BoneIndices.Length];
                for (var i = 0; i < ae.BoneIndices.Length; i++)
                    ae.Bones[i] = this.Bones[ae.BoneIndices[i]];
            }
        } catch (Exception e) {
            this.LoadException = e;
        }
    }

    public bool TryGetBoneByName(string name, [MaybeNullWhen(false)] out Bone bone) =>
        (bone = this.Bones.FirstOrDefault(x => x.Name == name)) != null;

    public class AlphEntry {
        public ushort[] BoneIndices;
        public Bone[]? Bones;
        public int Unk;

        public AlphEntry()
        {
            this.BoneIndices = [];
        }

        public AlphEntry(BinaryReader br)
        {
            this.Unk = br.ReadInt32();
            var dataCount = br.ReadUInt16();
            this.BoneIndices = br.ReadStructuresAsArray<ushort>(dataCount);
        }

        public override string ToString() => $"{this.Unk}; count={this.BoneIndices.Length}" +
            (this.Bones is null ? "" : string.Join("", this.Bones.Select(x => $"; {x.Name}")));
    }

    public class BoneList {
        private readonly List<Bone> _bones = [];
        private readonly Dictionary<string, int> _boneNameToIndex = new();
        private readonly Dictionary<Bone, int> _boneRemap = new();

        public void AddBones(IEnumerable<Bone> bones)
        {
            foreach (var b in bones) {
                if (this._boneNameToIndex.TryGetValue(b.Name, out var boneIndex)) {
                    this._boneRemap[b] = boneIndex;
                    continue;
                }

                if (b.Parent is not null)
                    this._bones.Add(new(this._bones.Count, this._bones[this._boneNameToIndex[b.Parent.Name]], b));
                else
                    this._bones.Add(new(this._bones.Count, this._bones.FirstOrDefault(), b));

                this._boneNameToIndex[this._bones.Last().Name] = this._bones.Count - 1;
                this._boneRemap[b] = this._bones.Count - 1;
            }
        }

        public bool TryGetIndex(string name, out int i) => this._boneNameToIndex.TryGetValue(name, out i);

        public int GetRemappedBoneIndex(Bone bone) => this._boneRemap[bone];

        public IReadOnlyList<Bone> Bones => this._bones;
    }

    public class Bone {
        private readonly List<Bone> _children = [];

        public readonly int Index;
        public readonly Bone? Parent;
        public readonly string Name;
        public readonly Vector3 Translation;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        public readonly Matrix4x4 BindPoseRelative;
        public readonly Matrix4x4 BindPoseAbsolute;
        public readonly Matrix4x4 BindPoseAbsoluteInverse;

        public Bone(Bone bone) :
            this(bone.Index, bone.Parent, bone.Name, bone.Translation, bone.Rotation, bone.Scale)
        { }

        public Bone(int index, Bone? parent, Bone bone) :
            this(index, parent, bone.Name, bone.Translation, bone.Rotation, bone.Scale)
        { }

        public Bone(int index, Bone? parent, string name, Vector3 translation, Quaternion rotation, Vector3 scale)
        {
            this.Index = index;
            this.Parent = parent;
            this.Name = name;
            this.Translation = translation;
            this.Rotation = rotation;
            this.Scale = scale;

            parent?._children.Add(this);

            this.BindPoseRelative =
                Matrix4x4.CreateScale(this.Scale) *
                Matrix4x4.CreateFromQuaternion(this.Rotation) *
                Matrix4x4.CreateTranslation(this.Translation);
            if (parent is null)
                this.BindPoseAbsolute = this.BindPoseRelative;
            else
                this.BindPoseAbsolute = this.BindPoseRelative * parent.BindPoseAbsolute;
            this.BindPoseAbsoluteInverse = Matrix4x4.Invert(this.BindPoseAbsolute, out var inverted)
                ? inverted
                : throw new InvalidDataException();
        }

        public IReadOnlyList<Bone> Children => this._children;

        public override string ToString() =>
            this._children.Count switch {
                0 => $"{this.Name}#{this.Index} (leaf)",
                1 => $"{this.Name}#{this.Index} (1 child)",
                var r => $"{this.Name}#{this.Index} ({r} children)",
            };
    }

    /// <summary>Version of the SE-specific header (not of the contained havok data).</summary>
    /// <remarks>Names are the version bytes as stored in file order; e.g. <see cref="K0021"/> is "1200" read as
    /// a big-endian number.</remarks>
    public enum SklbFormat : uint {
        K0011 = 0x31313030u,
        K0111 = 0x31313130u,
        K0021 = 0x31323030u,
        K0031 = 0x31333030u,
        K1031 = 0x31333031u,
    }

    public interface ISklbVersionedHeader {
        public int AlphOffset { get; }
        public int HavokOffset { get; }
        public SkeletonTargetModelClassification ModelClassification { get; }
        public int ModelId { get; }
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
            get => this.AlphOffsetU16;
            set => this.AlphOffsetU16 = checked((ushort) value);
        }

        public int HavokOffset {
            get => this.HavokOffsetU16;
            set => this.HavokOffsetU16 = checked((ushort) value);
        }

        public SkeletonTargetModelClassification ModelClassification {
            get => this.Common00210031.ModelClassification;
            set => this.Common00210031.ModelClassification = value;
        }

        public int ModelId {
            get => this.Common00210031.ModelId;
            set => this.Common00210031.ModelId = checked((ushort) value);
        }

        public ushort AlphOffsetU16;
        public ushort HavokOffsetU16;
        public SklbVersionedHeaderCommon00210031 Common00210031;
    }

    public struct Sklb0031 : ISklbVersionedHeader {
        public int AlphOffset {
            get => (int) this.AlphOffsetU32;
            set => this.AlphOffsetU32 = unchecked((uint) value);
        }

        public int HavokOffset {
            get => (int) this.HavokOffsetU32;
            set => this.HavokOffsetU32 = unchecked((uint) value);
        }

        public SkeletonTargetModelClassification ModelClassification {
            get => this.Common00210031.ModelClassification;
            set => this.Common00210031.ModelClassification = value;
        }

        public int ModelId {
            get => this.Common00210031.ModelId;
            set => this.Common00210031.ModelId = checked((ushort) value);
        }

        public uint AlphOffsetU32;
        public uint HavokOffsetU32;
        public uint Unknown1;
        public SklbVersionedHeaderCommon00210031 Common00210031;
    }
}
