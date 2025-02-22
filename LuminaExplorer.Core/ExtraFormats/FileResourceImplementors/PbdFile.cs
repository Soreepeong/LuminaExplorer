using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Extensions;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".pbd")]
public class PbdFile : FileResource {
    public HeaderBySkeletonId[] HeadersBySkeleton = null!;
    public HeaderByDeformerId[] HeadersByDeformer = null!;
    public Deformer[] Deformers = null!;

    public override void LoadFile()
    {
        var entryCount = this.Reader.ReadInt32();

        this.HeadersBySkeleton = this.Reader.ReadStructuresAsArray<HeaderBySkeletonId>(entryCount);
        this.HeadersByDeformer = this.Reader.ReadStructuresAsArray<HeaderByDeformerId>(entryCount);
        this.Deformers = new Deformer[entryCount];
        for (var i = 0; i < entryCount; i++) {
            var hbdi = this.HeadersByDeformer[i];
            var hbsi = this.HeadersBySkeleton[hbdi.SkeletonIndex];
            this.Deformers[i] = new(
                this.Reader,
                hbdi,
                hbsi,
                hbdi.ParentDeformerIndex == 0xFFFF ? null : this.Deformers[hbdi.ParentDeformerIndex]);
        }
    }

    public Deformer RootDeformer => this.Deformers.Single(x => x.Parent is null);

    public bool TryGetDeformerBySkeletonId(
        XivHumanSkeletonId skeletonId,
        [MaybeNullWhen(false)] out Deformer deformer)
    {
        foreach (var (s, d) in this.HeadersBySkeleton.Zip(this.Deformers)) {
            if (s.SkeletonId == skeletonId) {
                deformer = d;
                return true;
            }
        }

        deformer = new();
        return false;
    }

    public struct HeaderByDeformerId {
        public ushort ParentDeformerIndex;
        public ushort Unknown2;
        public ushort Unknown3;
        public ushort SkeletonIndex;

        public override string ToString() =>
            $"{this.ParentDeformerIndex}, {this.Unknown2:X04}, {this.Unknown3:X04}, {this.SkeletonIndex}";
    }

    public struct HeaderBySkeletonId {
        public XivHumanSkeletonId SkeletonId;
        public ushort DeformerId;
        public int Offset;
        public float BaseScale;

        public override string ToString() => $"{this.SkeletonId}, {this.DeformerId}, {this.BaseScale:0.000}";
    }

    public class Deformer {
        public XivHumanSkeletonId SkeletonId;
        public int DeformerId;
        public float BaseScale;
        public Deformer? Parent;
        public ushort HbdUnk2;
        public ushort HbdUnk3;

        public List<Deformer> Children = [];

        public int BoneCount = 0;
        public string[] BoneNames = [];
        public Matrix4x4[] Matrices = [];
        public Vector3[] Translations = [];
        public Quaternion[] Rotations = [];
        public Vector3[] Scales = [];

        public Deformer()
        { }

        public Deformer(
            BinaryReader reader,
            HeaderByDeformerId hbdi,
            HeaderBySkeletonId hbsi,
            Deformer? parentDeformer)
        {
            this.SkeletonId = hbsi.SkeletonId;
            this.DeformerId = hbsi.DeformerId;
            this.BaseScale = hbsi.BaseScale;
            this.Parent = parentDeformer;
            this.HbdUnk2 = hbdi.Unknown2;
            this.HbdUnk3 = hbdi.Unknown3;

            this.Parent?.Children.Add(this);

            if (hbsi.Offset == 0)
                return;

            reader.BaseStream.Position = hbsi.Offset;
            reader.ReadInto(out this.BoneCount);

            var nameOffsets = reader.ReadStructuresAsArray<ushort>(this.BoneCount);
            reader.WithAlign(4);

            this.Translations = new Vector3[this.BoneCount];
            this.Rotations = new Quaternion[this.BoneCount];
            this.Scales = new Vector3[this.BoneCount];
            this.Matrices = new Matrix4x4[this.BoneCount];
            for (var i = 0; i < this.BoneCount; i++) {
                this.Matrices[i] = new(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    0,
                    0,
                    0,
                    1
                );

                var transposed = Matrix4x4.Transpose(this.Matrices[i]);
                if (!Matrix4x4.Decompose(
                        transposed,
                        out this.Scales[i],
                        out this.Rotations[i],
                        out this.Translations[i])) {
                    // s @ r @ t = m
                    // s @ r = m @ t^-1
                    // r = s^-1 @ m @ t^-1
                    if (!Matrix4x4.Invert(Matrix4x4.CreateTranslation(this.Translations[i]), out var invTranslation))
                        throw new InvalidOperationException();
                    if (!Matrix4x4.Invert(Matrix4x4.CreateScale(this.Scales[i]), out var invScale))
                        throw new InvalidOperationException();
                    this.Rotations[i] = Quaternion.CreateFromRotationMatrix(invScale * transposed * invTranslation);
                }
            }

            this.BoneNames = nameOffsets
                .Select(x => reader.WithSeek(hbsi.Offset + x, SeekOrigin.Begin).ReadCString())
                .ToArray();
        }

        public override string ToString() =>
            $"{this.SkeletonId} @ {this.DeformerId} (x{this.BaseScale:0.00}); {this.HbdUnk2}, {this.HbdUnk3}";
    }
}
