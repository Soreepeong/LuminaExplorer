using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Extensions;
using Lumina.Models.Models;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// <see cref="MdlFile"/> with a parser that understands v6 (Dawntrail) models, based on Penumbra's MdlFile.
/// </summary>
/// <remarks>
/// <para>Lumina (as of 7.7.1) always reads bone tables in the v5 layout (u16[64] indices + u32 count per table).
/// v6 instead stores a (u16 offset, u16 count) header per table, followed by a shared u16 index array of
/// <see cref="BoneTableArrayCountTotal"/> entries, which lets a table reference more than 64 bones. Reading a v6 file
/// with the v5 layout misplaces everything after the bone tables (shapes, submesh bone map, bounding boxes), and
/// usually runs past the end of the data.</para>
/// <para>v6 models may also have neck morph data (<see cref="NeckMorphCount"/> entries) followed by
/// <c>ModelHeader.Unknown9</c> 16-byte records between the submesh bone map and the bounding box padding.</para>
/// <para>All fields of <see cref="MdlFile"/> are populated as Lumina would, so that existing consumers (including
/// <see cref="Lumina.Models.Models.Model"/>) keep working. For v6 files, each
/// <see cref="MdlStructs.BoneTableStruct.BoneIndex"/> holds exactly the bones of that table, instead of 64 entries.
/// </para>
/// <para>This class is substituted for <see cref="MdlFile"/> whenever a file resource is loaded through the virtual
/// file system.</para>
/// </remarks>
public class PenumbraMdlFile : MdlFile {
    public const uint V5 = 0x01000005;
    public const uint V6 = 0x01000006;

    public const int UnknownData9EntrySize = 16;

    public NeckMorphStruct[] NeckMorphs = [];

    /// <summary>Raw data of <c>ModelHeader.Unknown9</c> entries of <see cref="UnknownData9EntrySize"/> bytes each.
    /// </summary>
    public byte[] UnknownData9 = [];

    public byte NeckMorphCount => this.ModelHeader.Unknown6;

    public ushort BoneTableArrayCountTotal => this.ModelHeader.Unknown7;

    public bool IsV6 => this.FileHeader.Version >= V6;

    public override void LoadFile()
    {
        var isLittleEndian = this.Reader.IsLittleEndian;
        this.Reader.IsLittleEndian = BitConverter.IsLittleEndian;
        this.FileHeader = MdlStructs.ModelFileHeader.Read(this.Reader);
        this.Reader.IsLittleEndian = isLittleEndian;

        this.VertexDeclarations = new MdlStructs.VertexDeclarationStruct[this.FileHeader.VertexDeclarationCount];
        for (var i = 0; i < this.VertexDeclarations.Length; i++)
            this.VertexDeclarations[i] = MdlStructs.VertexDeclarationStruct.Read(this.Reader);

        this.StringCount = this.Reader.ReadUInt16();
        this.Reader.ReadUInt16();
        var stringSize = this.Reader.ReadUInt32();
        this.Strings = this.Reader.ReadBytes((int) stringSize);

        this.ModelHeader = this.Reader.ReadStructure<MdlStructs.ModelHeader>();

        this.ElementIds = new MdlStructs.ElementIdStruct[this.ModelHeader.ElementIdCount];
        for (var i = 0; i < this.ElementIds.Length; i++)
            this.ElementIds[i] = MdlStructs.ElementIdStruct.Read(this.Reader);

        this.Lods = this.Reader.ReadStructuresAsArray<MdlStructs.LodStruct>(3);
        this.ExtraLods = this.ModelHeader.ExtraLodEnabled
            ? this.Reader.ReadStructuresAsArray<MdlStructs.ExtraLodStruct>(3)
            : [];

        this.Meshes = new MdlStructs.MeshStruct[this.ModelHeader.MeshCount];
        for (var i = 0; i < this.Meshes.Length; i++)
            this.Meshes[i] = MdlStructs.MeshStruct.Read(this.Reader);

        this.AttributeNameOffsets = this.Reader.ReadUInt32Array(this.ModelHeader.AttributeCount);
        this.TerrainShadowMeshes =
            this.Reader.ReadStructuresAsArray<MdlStructs.TerrainShadowMeshStruct>(
                this.ModelHeader.TerrainShadowMeshCount);
        this.Submeshes = this.Reader.ReadStructuresAsArray<MdlStructs.SubmeshStruct>(this.ModelHeader.SubmeshCount);
        this.TerrainShadowSubmeshes =
            this.Reader.ReadStructuresAsArray<MdlStructs.TerrainShadowSubmeshStruct>(
                this.ModelHeader.TerrainShadowSubmeshCount);

        this.MaterialNameOffsets = this.Reader.ReadUInt32Array(this.ModelHeader.MaterialCount);
        this.BoneNameOffsets = this.Reader.ReadUInt32Array(this.ModelHeader.BoneCount);

        this.BoneTables = this.IsV6
            ? this.ReadBoneTablesV6()
            : this.ReadBoneTablesV5();

        this.Shapes = new MdlStructs.ShapeStruct[this.ModelHeader.ShapeCount];
        for (var i = 0; i < this.Shapes.Length; i++)
            this.Shapes[i] = MdlStructs.ShapeStruct.Read(this.Reader);
        this.ShapeMeshes = this.Reader.ReadStructuresAsArray<MdlStructs.ShapeMeshStruct>(this.ModelHeader.ShapeMeshCount);
        this.ShapeValues = this.Reader.ReadStructuresAsArray<MdlStructs.ShapeValueStruct>(this.ModelHeader.ShapeValueCount);

        var submeshBoneMapSize = this.Reader.ReadUInt32();
        this.SubmeshBoneMap = this.Reader.ReadUInt16Array((int) submeshBoneMapSize / 2);

        if (this.IsV6) {
            this.NeckMorphs = this.Reader.ReadStructuresAsArray<NeckMorphStruct>(this.NeckMorphCount);
            this.UnknownData9 = this.Reader.ReadBytes(this.ModelHeader.Unknown9 * UnknownData9EntrySize);
        }

        var paddingAmount = this.Reader.ReadByte();
        this.Reader.Seek(this.Reader.BaseStream.Position + paddingAmount);

        this.BoundingBoxes = MdlStructs.BoundingBoxStruct.Read(this.Reader);
        this.ModelBoundingBoxes = MdlStructs.BoundingBoxStruct.Read(this.Reader);
        this.WaterBoundingBoxes = MdlStructs.BoundingBoxStruct.Read(this.Reader);
        this.VerticalFogBoundingBoxes = MdlStructs.BoundingBoxStruct.Read(this.Reader);
        this.BoneBoundingBoxes = new MdlStructs.BoundingBoxStruct[this.ModelHeader.BoneCount];
        for (var i = 0; i < this.BoneBoundingBoxes.Length; i++)
            this.BoneBoundingBoxes[i] = MdlStructs.BoundingBoxStruct.Read(this.Reader);
    }

    private MdlStructs.BoneTableStruct[] ReadBoneTablesV5()
    {
        var tables = new MdlStructs.BoneTableStruct[this.ModelHeader.BoneTableCount];
        for (var i = 0; i < tables.Length; i++)
            tables[i] = MdlStructs.BoneTableStruct.Read(this.Reader);
        return tables;
    }

    private MdlStructs.BoneTableStruct[] ReadBoneTablesV6()
    {
        var count = this.ModelHeader.BoneTableCount;
        var offsets = new ushort[count];
        var sizes = new ushort[count];
        for (var i = 0; i < count; i++) {
            offsets[i] = this.Reader.ReadUInt16();
            sizes[i] = this.Reader.ReadUInt16();
        }

        var indices = this.Reader.ReadUInt16Array(this.BoneTableArrayCountTotal);

        var tables = new MdlStructs.BoneTableStruct[count];
        for (var i = 0; i < count; i++) {
            // Offset is in units of 4 bytes, relative to the position of the table's own (offset, size) header.
            var start = (i + offsets[i] - count) * 2;
            if (start < 0 || start + sizes[i] > indices.Length) {
                throw new InvalidDataException(
                    $"Bone table #{i} (offset={offsets[i]}, size={sizes[i]}) is out of range " +
                    $"of the bone index array ({indices.Length} entries).");
            }

            tables[i] = new() {
                BoneIndex = indices.AsSpan(start, sizes[i]).ToArray(),
                BoneCount = (byte) Math.Min(sizes[i], (ushort) byte.MaxValue),
            };
        }

        return tables;
    }

    /// <summary>Creates a <see cref="Model"/> from the given file, working around unsupported vertex formats.</summary>
    /// <remarks>
    /// <see cref="Model"/> (as of Lumina 7.7.1) only understands vertex element types Single3, Single4, UByte4,
    /// NByte4, Half2 and Half4, reads elements back to back ignoring their declared offsets and the stream strides,
    /// and throws on UByte4 blend weights. v6 models use UByte4 blend weights, and UShort4 (8 bytes) blend weights and
    /// indices for meshes with up to 8 influences per vertex. The model is built with the vertex declarations hidden
    /// from Lumina, and the vertices are filled in afterwards. Vertices with more than 4 influences keep the 4 largest
    /// ones, renormalized.
    /// </remarks>
    public static Model CreateModel(MdlFile mdlFile, Model.ModelLod lod = Model.ModelLod.High, int variantId = 1)
    {
        if (mdlFile is not PenumbraMdlFile pmf)
            return new(mdlFile, lod, variantId);

        var clone = (PenumbraMdlFile) pmf.MemberwiseClone();
        clone.VertexDeclarations = pmf.VertexDeclarations
            .Select(_ => new MdlStructs.VertexDeclarationStruct { VertexElements = [] })
            .ToArray();

        var model = new Model(clone, lod, variantId);
        typeof(Model).GetProperty(nameof(Model.File))!.SetValue(model, pmf);
        foreach (var mesh in model.Meshes)
            mesh.Vertices = pmf.ReadVertices(mesh.MeshIndex, (int) model.Lod);
        return model;
    }

    private Vertex[] ReadVertices(int meshIndex, int lod)
    {
        var mesh = this.Meshes[meshIndex];
        var elements = this.VertexDeclarations[meshIndex].VertexElements;
        var lodBase = (int) this.FileHeader.VertexOffset[lod];
        var vertices = new Vertex[mesh.VertexCount];
        for (var i = 0; i < vertices.Length; i++) {
            float[]? weights = null;
            byte[]? indices = null;
            foreach (var element in elements) {
                var offset = lodBase +
                    (int) mesh.VertexBufferOffset[element.Stream] +
                    i * mesh.VertexBufferStride[element.Stream] +
                    element.Offset;
                var type = (VertexType) element.Type;
                var data = this.Data.AsSpan(offset, GetVertexElementSize(type));
                switch ((VertexUsage) element.Usage) {
                    case VertexUsage.Position:
                        vertices[i].Position = ReadVector4(type, data);
                        break;
                    case VertexUsage.BlendWeights:
                        if (type is VertexType.UByte4 or VertexType.UShort2 or VertexType.UShort4) {
                            weights = new float[data.Length];
                            for (var j = 0; j < data.Length; j++)
                                weights[j] = data[j] / 255f;
                        } else {
                            var w = ReadVector4(type, data);
                            weights = [w.X, w.Y, w.Z, w.W];
                        }

                        break;
                    case VertexUsage.BlendIndices:
                        indices = data.ToArray();
                        break;
                    case VertexUsage.Normal: {
                        var n = ReadVector4(type, data);
                        vertices[i].Normal = new(n.X, n.Y, n.Z);
                        break;
                    }
                    case VertexUsage.UV:
                        vertices[i].UV = ReadVector4(type, data);
                        break;
                    case VertexUsage.Tangent2:
                        vertices[i].Tangent2 = ReadVector4(type, data);
                        break;
                    case VertexUsage.Tangent1:
                        vertices[i].Tangent1 = ReadVector4(type, data);
                        break;
                    case VertexUsage.Color:
                        vertices[i].Color = ReadVector4(type, data);
                        break;
                }
            }

            if (weights is { Length: > 4 } && indices is { Length: > 4 })
                KeepLargestFourInfluences(ref weights, ref indices);

            if (weights is not null) {
                vertices[i].BlendWeights = new(
                    weights.ElementAtOrDefault(0),
                    weights.ElementAtOrDefault(1),
                    weights.ElementAtOrDefault(2),
                    weights.ElementAtOrDefault(3));
            }

            if (indices is not null)
                vertices[i].BlendIndices = indices.Length > 4 ? indices[..4] : indices;
        }

        return vertices;
    }

    private static void KeepLargestFourInfluences(ref float[] weights, ref byte[] indices)
    {
        var w = weights;
        var b = indices;
        var top = Enumerable.Range(0, Math.Min(w.Length, b.Length))
            .OrderByDescending(x => w[x])
            .Take(4)
            .ToArray();
        var newWeights = top.Select(x => w[x]).ToArray();
        var newIndices = top.Select(x => b[x]).ToArray();

        var sum = newWeights.Sum();
        if (sum > 0) {
            for (var j = 0; j < newWeights.Length; j++)
                newWeights[j] /= sum;
        }

        weights = newWeights;
        indices = newIndices;
    }

    private static Vector4 ReadVector4(VertexType type, ReadOnlySpan<byte> data)
    {
        switch (type) {
            case VertexType.Single1:
                return new(BitConverter.ToSingle(data), 0, 0, 0);
            case VertexType.Single2:
                return new(BitConverter.ToSingle(data), BitConverter.ToSingle(data[4..]), 0, 0);
            case VertexType.Single3:
                return new(
                    BitConverter.ToSingle(data),
                    BitConverter.ToSingle(data[4..]),
                    BitConverter.ToSingle(data[8..]),
                    1);
            case VertexType.Single4:
                return new(
                    BitConverter.ToSingle(data),
                    BitConverter.ToSingle(data[4..]),
                    BitConverter.ToSingle(data[8..]),
                    BitConverter.ToSingle(data[12..]));
            case VertexType.UByte4:
                return new(data[0], data[1], data[2], data[3]);
            case VertexType.NByte4:
                return new(data[0] / 255f, data[1] / 255f, data[2] / 255f, data[3] / 255f);
            case VertexType.Short2:
                return new(BitConverter.ToInt16(data), BitConverter.ToInt16(data[2..]), 0, 0);
            case VertexType.Short4:
                return new(
                    BitConverter.ToInt16(data),
                    BitConverter.ToInt16(data[2..]),
                    BitConverter.ToInt16(data[4..]),
                    BitConverter.ToInt16(data[6..]));
            case VertexType.NShort2:
                return new(BitConverter.ToInt16(data) / 32767f, BitConverter.ToInt16(data[2..]) / 32767f, 0, 0);
            case VertexType.NShort4:
                return new(
                    BitConverter.ToInt16(data) / 32767f,
                    BitConverter.ToInt16(data[2..]) / 32767f,
                    BitConverter.ToInt16(data[4..]) / 32767f,
                    BitConverter.ToInt16(data[6..]) / 32767f);
            case VertexType.Half2:
                return new((float) BitConverter.ToHalf(data), (float) BitConverter.ToHalf(data[2..]), 0, 0);
            case VertexType.Half4:
                return new(
                    (float) BitConverter.ToHalf(data),
                    (float) BitConverter.ToHalf(data[2..]),
                    (float) BitConverter.ToHalf(data[4..]),
                    (float) BitConverter.ToHalf(data[6..]));
            case VertexType.UShort2:
                return new(BitConverter.ToUInt16(data), BitConverter.ToUInt16(data[2..]), 0, 0);
            case VertexType.UShort4:
                return new(
                    BitConverter.ToUInt16(data),
                    BitConverter.ToUInt16(data[2..]),
                    BitConverter.ToUInt16(data[4..]),
                    BitConverter.ToUInt16(data[6..]));
            default:
                throw new NotSupportedException($"Unsupported vertex element type {type}");
        }
    }

    private static int GetVertexElementSize(VertexType type) => type switch {
        VertexType.Single1 => 4,
        VertexType.Single2 => 8,
        VertexType.Single3 => 12,
        VertexType.Single4 => 16,
        VertexType.UByte4 => 4,
        VertexType.Short2 => 4,
        VertexType.Short4 => 8,
        VertexType.NByte4 => 4,
        VertexType.NShort2 => 4,
        VertexType.NShort4 => 8,
        VertexType.Half2 => 4,
        VertexType.Half4 => 8,
        VertexType.UShort2 => 4,
        VertexType.UShort4 => 8,
        _ => throw new NotSupportedException($"Unsupported vertex element type {type}"),
    };

    public enum VertexType : byte {
        Single1 = 0,
        Single2 = 1,
        Single3 = 2,
        Single4 = 3,
        UByte4 = 5,
        Short2 = 6,
        Short4 = 7,
        NByte4 = 8,
        NShort2 = 9,
        NShort4 = 10,
        Half2 = 13,
        Half4 = 14,
        UShort2 = 16,
        UShort4 = 17,
    }

    public enum VertexUsage : byte {
        Position = 0,
        BlendWeights = 1,
        BlendIndices = 2,
        Normal = 3,
        UV = 4,
        Tangent2 = 5,
        Tangent1 = 6,
        Color = 7,
    }

    public struct NeckMorphStruct {
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public uint ConstValue;
        public float NormalX;
        public float NormalY;
        public float NormalZ;
        public byte BoneIndex1;
        public byte BoneIndex2;
        public byte BoneIndex3;
        public byte BoneIndex4;
    }
}
