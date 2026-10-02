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
    public const ushort NoIndex = 0xFFFF;

    /// <summary>Deformer headers; one per skeleton (gender/race).</summary>
    public HeaderBySkeletonId[] HeadersBySkeleton = null!;

    /// <summary>Racial tree entries; <see cref="HeaderBySkeletonId.DeformerId"/> indexes into this array.</summary>
    public HeaderByDeformerId[] HeadersByDeformer = null!;

    /// <summary>Deformers, indexed by tree entry index (same index as <see cref="HeadersByDeformer"/>).</summary>
    public Deformer[] Deformers = null!;

    public override void LoadFile()
    {
        var entryCount = this.Reader.ReadInt32();

        this.HeadersBySkeleton = this.Reader.ReadStructuresAsArray<HeaderBySkeletonId>(entryCount);
        this.HeadersByDeformer = this.Reader.ReadStructuresAsArray<HeaderByDeformerId>(entryCount);
        this.Deformers = new Deformer[entryCount];

        // Create every node first; parents may come after their children in the tree array.
        for (var i = 0; i < entryCount; i++) {
            var hbdi = this.HeadersByDeformer[i];
            var hbsi = hbdi.SkeletonIndex < entryCount
                ? this.HeadersBySkeleton[hbdi.SkeletonIndex]
                : new() { DeformerId = (ushort) i };
            this.Deformers[i] = new(this.Reader, hbdi, hbsi, null);
        }

        // Link the tree; follow the first child/next sibling chain to keep the order defined by the file.
        for (var i = 0; i < entryCount; i++) {
            var hbdi = this.HeadersByDeformer[i];
            if (hbdi.ParentDeformerIndex < entryCount)
                this.Deformers[i].Parent = this.Deformers[hbdi.ParentDeformerIndex];

            var visited = new HashSet<int>();
            for (var child = (int) hbdi.FirstChildIndex;
                 child < entryCount && visited.Add(child);
                 child = this.HeadersByDeformer[child].NextSiblingIndex) {
                this.Deformers[i].Children.Add(this.Deformers[child]);
            }
        }

        // Fallback for children not reachable through the sibling chain.
        foreach (var d in this.Deformers) {
            if (d.Parent is { } parent && !parent.Children.Contains(d))
                parent.Children.Add(d);
        }
    }

    public Deformer RootDeformer => this.Deformers.Single(x => x.Parent is null);

    public bool TryGetDeformerBySkeletonId(
        XivHumanSkeletonId skeletonId,
        [MaybeNullWhen(false)] out Deformer deformer)
    {
        // Deformers are indexed by deformer (tree node) index, not by skeleton header index.
        foreach (var d in this.Deformers) {
            if (d.SkeletonId == skeletonId) {
                deformer = d;
                return true;
            }
        }

        deformer = new();
        return false;
    }

    /// <summary>Racial tree entry. Indices of <see cref="NoIndex"/> (-1) denote absence.</summary>
    public struct HeaderByDeformerId {
        /// <summary>Index of the parent tree entry.</summary>
        public ushort ParentDeformerIndex;

        /// <summary>Index of the first child tree entry.</summary>
        public ushort FirstChildIndex;

        /// <summary>Index of the next sibling tree entry.</summary>
        public ushort NextSiblingIndex;

        /// <summary>Index into <see cref="PbdFile.HeadersBySkeleton"/>.</summary>
        public ushort SkeletonIndex;

        public ushort Unknown2 {
            readonly get => this.FirstChildIndex;
            set => this.FirstChildIndex = value;
        }

        public ushort Unknown3 {
            readonly get => this.NextSiblingIndex;
            set => this.NextSiblingIndex = value;
        }

        public readonly override string ToString() =>
            $"parent={(short) this.ParentDeformerIndex}, child={(short) this.FirstChildIndex}, " +
            $"sibling={(short) this.NextSiblingIndex}, header={this.SkeletonIndex}";
    }

    /// <summary>Deformer header.</summary>
    public struct HeaderBySkeletonId {
        /// <summary>Gender/race code of the skeleton.</summary>
        public XivHumanSkeletonId SkeletonId;

        /// <summary>Index into <see cref="PbdFile.HeadersByDeformer"/> (the racial tree).</summary>
        public ushort DeformerId;

        /// <summary>Offset of the deformer data from the beginning of the file, or 0 if there is none.</summary>
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

        /// <summary>Index of the deformer header in <see cref="PbdFile.HeadersBySkeleton"/>.</summary>
        public int HeaderIndex = -1;

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
            this.HbdUnk2 = hbdi.FirstChildIndex;
            this.HbdUnk3 = hbdi.NextSiblingIndex;
            this.HeaderIndex = hbdi.SkeletonIndex;

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

        /// <summary>Index of the first child in <see cref="PbdFile.Deformers"/>, or <see cref="NoIndex"/>.</summary>
        public ushort FirstChildIndex => this.HbdUnk2;

        /// <summary>Index of the next sibling in <see cref="PbdFile.Deformers"/>, or <see cref="NoIndex"/>.</summary>
        public ushort NextSiblingIndex => this.HbdUnk3;

        public bool TryGetMatrix(string boneName, out Matrix4x4 matrix)
        {
            var i = Array.IndexOf(this.BoneNames, boneName);
            if (i < 0) {
                matrix = Matrix4x4.Identity;
                return false;
            }

            matrix = this.Matrices[i];
            return true;
        }

        public override string ToString() =>
            $"{this.SkeletonId} @ {this.DeformerId} (x{this.BaseScale:0.00}); {this.HbdUnk2}, {this.HbdUnk3}";
    }
}
