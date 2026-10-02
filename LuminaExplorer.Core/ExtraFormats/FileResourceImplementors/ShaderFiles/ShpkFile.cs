using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Misc;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shpk")]
public class ShpkFile : FileResource {
    public const uint SelectorMultiplier = 31;

    public ShpkHeader Header;

    // Header fields present since version 0x0D01.
    public uint HullShaderCount;
    public uint DomainShaderCount;
    public uint GeometryShaderCount;

    // Header field present since version 0x0E01.
    public uint NodeAliasClusterCount;

    public ShaderEntry[] VertexShaderEntries = [];
    public ShaderEntry[] PixelShaderEntries = [];
    public ShaderEntry[] HullShaderEntries = [];
    public ShaderEntry[] DomainShaderEntries = [];
    public ShaderEntry[] GeometryShaderEntries = [];
    public ShaderMaterialParam[] MaterialParams = [];
    public byte[] MaterialParamsDefaults = [];
    public ShaderInput[] Constants = [];
    public ShaderInput[] Samplers = [];
    public ShaderInput[] Textures = [];
    public ShaderInput[] Uavs = [];
    public ShaderKey[] SystemKeys = [];
    public ShaderKey[] SceneKeys = [];
    public ShaderKey[] MaterialKeys = [];
    public ShaderKey[] SubViewKeys = [];
    public ShaderNode[] Nodes = [];
    public ShaderNodeAlias[] NodeAliases = [];
    public ShaderNodeAliasCluster[] NodeAliasClusters = [];

    /// <summary>Data between the end of the parsed tables and the blob section. Usually empty.</summary>
    public byte[] AdditionalData = [];

    /// <summary>Map from selector to index of <see cref="Nodes"/>, including both nodes and node aliases.</summary>
    public readonly Dictionary<uint, uint> NodeSelectors = new();

    public uint Version => this.Header.Version;

    /// <summary>
    /// Whether this is a legacy (pre-0x0D01) shader package without separate texture resources, where samplers double
    /// as textures.
    /// </summary>
    public bool IsLegacy =>
        this.Header.Version < (uint) ShpkVersion.V0D01 &&
        this.Header.HasMaterialParamDefaults == 0 &&
        this.Header.TextureCount == 0;

    public IEnumerable<ShaderEntry> AllShaderEntries =>
        this.VertexShaderEntries
            .Concat(this.PixelShaderEntries)
            .Concat(this.HullShaderEntries)
            .Concat(this.DomainShaderEntries)
            .Concat(this.GeometryShaderEntries);

    public override void LoadFile()
    {
        this.Header = this.Reader.ReadStructure<ShpkHeader>();
        if (this.Header.Magic != ShpkHeader.MagicValue)
            throw new InvalidDataException();

        if (this.Header.Version >= (uint) ShpkVersion.V0D01) {
            this.HullShaderCount = this.Reader.ReadUInt32();
            this.DomainShaderCount = this.Reader.ReadUInt32();
            this.GeometryShaderCount = this.Reader.ReadUInt32();
        }

        if (this.Header.Version >= (uint) ShpkVersion.V0E01)
            this.NodeAliasClusterCount = this.Reader.ReadUInt32();

        this.VertexShaderEntries = this.ReadShaderEntries(this.Header.VertexShaderCount, ShaderType.Vertex);
        this.PixelShaderEntries = this.ReadShaderEntries(this.Header.PixelShaderCount, ShaderType.Pixel);
        this.HullShaderEntries = this.ReadShaderEntries(this.HullShaderCount, ShaderType.HullShader);
        this.DomainShaderEntries = this.ReadShaderEntries(this.DomainShaderCount, ShaderType.DomainShader);
        this.GeometryShaderEntries = this.ReadShaderEntries(this.GeometryShaderCount, ShaderType.Geometry);

        this.MaterialParams = this.Reader.ReadStructuresAsArray<ShaderMaterialParam>(this.Header.MaterialParamCount);
        this.MaterialParamsDefaults = this.Header.HasMaterialParamDefaults != 0
            ? this.Reader.ReadBytes((int) this.Header.MaterialParamSize)
            : [];

        this.Constants = this.Reader.ReadStructuresAsArray<ShaderInput>((int) this.Header.ConstantCount);
        this.Samplers = this.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.SamplerCount);
        this.Textures = this.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.TextureCount);
        this.Uavs = this.Reader.ReadStructuresAsArray<ShaderInput>((int) this.Header.UavCount);

        this.SystemKeys = this.Reader.ReadStructuresAsArray<ShaderKey>((int) this.Header.SystemKeyCount);
        this.SceneKeys = this.Reader.ReadStructuresAsArray<ShaderKey>((int) this.Header.SceneKeyCount);
        this.MaterialKeys = this.Reader.ReadStructuresAsArray<ShaderKey>((int) this.Header.MaterialKeyCount);

        this.SubViewKeys = [
            new() { Id = 1, DefaultValue = this.Reader.ReadUInt32() },
            new() { Id = 2, DefaultValue = this.Reader.ReadUInt32() },
        ];

        var hasExtendedNodes = this.Header.Version >= (uint) ShpkVersion.V0D01;
        this.Nodes = new ShaderNode[this.Header.NodeCount];
        for (var i = 0; i < this.Nodes.Length; i++) {
            this.Nodes[i].Id = this.Reader.ReadUInt32();
            var passCount = this.Reader.ReadUInt32();
            this.Nodes[i].PassIndices = this.Reader.ReadBytes(16);
            this.Nodes[i].Unk131Keys = hasExtendedNodes ? this.Reader.ReadStructuresAsArray<uint>(2) : [];
            this.Nodes[i].SystemKeys = this.Reader.ReadStructuresAsArray<uint>(this.SystemKeys.Length);
            this.Nodes[i].SceneKeys = this.Reader.ReadStructuresAsArray<uint>(this.SceneKeys.Length);
            this.Nodes[i].MaterialKeys = this.Reader.ReadStructuresAsArray<uint>(this.MaterialKeys.Length);
            this.Nodes[i].SubViewKeys = this.Reader.ReadStructuresAsArray<uint>(this.SubViewKeys.Length);
            if (!hasExtendedNodes)
                this.Nodes[i].Unk131Keys = (uint[]) this.Nodes[i].SubViewKeys.Clone();

            this.Nodes[i].Passes = new ShaderNodePass[passCount];
            for (var j = 0; j < this.Nodes[i].Passes.Length; j++) {
                this.Nodes[i].Passes[j] = new() {
                    Id = this.Reader.ReadUInt32(),
                    VertexShader = this.Reader.ReadUInt32(),
                    PixelShader = this.Reader.ReadUInt32(),
                    HullShader = hasExtendedNodes ? this.Reader.ReadUInt32() : uint.MaxValue,
                    DomainShader = hasExtendedNodes ? this.Reader.ReadUInt32() : uint.MaxValue,
                    GeometryShader = hasExtendedNodes ? this.Reader.ReadUInt32() : uint.MaxValue,
                };
            }
        }

        this.NodeAliases = this.Reader.ReadStructuresAsArray<ShaderNodeAlias>((int) this.Header.NodeAliasCount);

        this.NodeAliasClusters = new ShaderNodeAliasCluster[this.NodeAliasClusterCount];
        for (var i = 0; i < this.NodeAliasClusters.Length; i++) {
            this.NodeAliasClusters[i].SubViewValue2 = this.Reader.ReadUInt32();
            this.NodeAliasClusters[i].SubViewValue1 = this.Reader.ReadUInt32();
            var subClusterCount = this.Reader.ReadUInt32();
            this.NodeAliasClusters[i].Unknown = this.Reader.ReadUInt32();
            this.NodeAliasClusters[i].SubClusters = new ShaderNodeAliasSubCluster[subClusterCount];
            for (var j = 0; j < this.NodeAliasClusters[i].SubClusters.Length; j++)
                this.NodeAliasClusters[i].SubClusters[j] = new(this.Reader);
        }

        var remaining = this.Header.BlobOffset - this.Reader.BaseStream.Position;
        this.AdditionalData = remaining > 0 ? this.Reader.ReadBytes((int) remaining) : [];

        this.NodeSelectors.Clear();
        for (var i = 0; i < this.Nodes.Length; i++)
            this.NodeSelectors.TryAdd(this.Nodes[i].Id, (uint) i);
        foreach (var alias in this.NodeAliases)
            this.NodeSelectors.TryAdd(alias.Selector, alias.Node);
    }

    public bool TryGetNodeBySelector(uint selector, out ShaderNode node)
    {
        if (this.NodeSelectors.TryGetValue(selector, out var index) && index < this.Nodes.Length) {
            node = this.Nodes[index];
            return true;
        }

        node = default;
        return false;
    }

    public ReadOnlySpan<byte> GetMaterialParamDefault(ShaderMaterialParam param)
    {
        if (param.ByteOffset >= this.MaterialParamsDefaults.Length)
            return [];
        var end = Math.Min(param.ByteOffset + param.ByteSize, this.MaterialParamsDefaults.Length);
        return this.MaterialParamsDefaults.AsSpan(param.ByteOffset, end - param.ByteOffset);
    }

    public static uint BuildSelector(IEnumerable<uint> keys)
    {
        unchecked {
            var selector = 0u;
            var multiplier = 1u;
            foreach (var key in keys) {
                selector += key * multiplier;
                multiplier *= SelectorMultiplier;
            }

            return selector;
        }
    }

    public static uint BuildSelector(
        IEnumerable<uint> systemKeys,
        IEnumerable<uint> sceneKeys,
        IEnumerable<uint> materialKeys,
        IEnumerable<uint> subViewKeys) =>
        BuildSelector(
            new[] {
                BuildSelector(systemKeys),
                BuildSelector(sceneKeys),
                BuildSelector(materialKeys),
                BuildSelector(subViewKeys),
            });

    private ShaderEntry[] ReadShaderEntries(uint count, ShaderType shaderType) =>
        Enumerable.Range(0, (int) count).Select(_ => new ShaderEntry(this, shaderType)).ToArray();

    public class ShaderEntry : IShaderEntry {
        private readonly ShpkFile _file;

        public ShaderEntry(ShpkFile file, ShaderType shaderType)
        {
            this._file = file;
            this.ShaderType = shaderType;
            this.Header = this._file.Reader.ReadStructure<ShaderHeader>();

            // Observed to be 1 for vertex shaders and 4 for pixel shaders; only stored since version 0x0D01.
            this.Unk131 = this._file.Header.Version >= (uint) ShpkVersion.V0D01
                ? this._file.Reader.ReadUInt32()
                : shaderType switch {
                    ShaderType.Vertex => 1u,
                    ShaderType.Pixel => 4u,
                    ShaderType.Geometry => 8u,
                    _ => 0u,
                };

            this.InputTables = this._file.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.NumInputs);
            this.InputNames = this.InputTables.Select(
                x => Encoding.UTF8.GetString(
                    this._file.Data,
                    (int) (this._file.Header.StringsOffset + x.InputStringOffset),
                    x.InputStringSize)).ToArray();

            // Vertex shader blobs are prefixed by a header containing the declared (DX9 and DX11) and used (DX11 only)
            // vertex input masks, which is not a part of the shader bytecode.
            this.AdditionalHeaderSize = shaderType == ShaderType.Vertex
                ? (int) Math.Min(
                    this._file.Header.DirectXVersion == DirectXVersion.Dx9 ? 4u : 8u,
                    this.Header.BlobSize)
                : 0;
        }

        public ShaderHeader Header { get; set; }
        public ShaderInput[] InputTables { get; set; }
        public string[] InputNames { get; set; }

        /// <summary>Unknown value; synthesized from the shader type for versions before 0x0D01.</summary>
        public uint Unk131 { get; set; }

        public int AdditionalHeaderSize { get; }

        // Order of resources in InputTables: constants, samplers, UAVs, textures.
        public ArraySegment<ShaderInput> Constants => new(this.InputTables, 0, this.Header.ConstantCount);

        public ArraySegment<ShaderInput> Samplers =>
            new(this.InputTables, this.Header.ConstantCount, this.Header.SamplerCount);

        public ArraySegment<ShaderInput> Uavs =>
            new(this.InputTables, this.Header.ConstantCount + this.Header.SamplerCount, this.Header.UavCount);

        public ArraySegment<ShaderInput> Textures =>
            new(
                this.InputTables,
                this.Header.ConstantCount + this.Header.SamplerCount + this.Header.UavCount,
                this.Header.TextureCount);

        /// <summary>Raw blob, including <see cref="AdditionalHeader"/>.</summary>
        public ReadOnlySpan<byte> RawBlob =>
            this._file.DataSpan.Slice(
                (int) (this._file.Header.BlobOffset + this.Header.BlobOffset),
                (int) this.Header.BlobSize);

        public ReadOnlySpan<byte> AdditionalHeader => this.RawBlob[..this.AdditionalHeaderSize];

        /// <summary>Vertex input mask declared by the vertex shader.</summary>
        public uint DeclaredVertexInputs =>
            this.AdditionalHeaderSize >= 4 ? BitConverter.ToUInt32(this.AdditionalHeader[..4]) : 0u;

        /// <summary>Vertex input mask used by the vertex shader (DX11 only).</summary>
        public uint UsedVertexInputs =>
            this.AdditionalHeaderSize >= 8 ? BitConverter.ToUInt32(this.AdditionalHeader[4..8]) : 0u;

        /// <summary>Shader bytecode (DXBC for DX11), excluding <see cref="AdditionalHeader"/>.</summary>
        public ReadOnlySpan<byte> ByteCode => this.RawBlob[this.AdditionalHeaderSize..];

        public uint ByteCodeCrc32 => Crc32.Get(this.ByteCode);

        public ShaderType ShaderType { get; }

        public override string ToString() => this.Header.ToString();
    }
}
