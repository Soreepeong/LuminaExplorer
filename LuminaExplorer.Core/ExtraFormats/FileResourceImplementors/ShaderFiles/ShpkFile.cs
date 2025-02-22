using System;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Misc;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shpk")]
public class ShpkFile : FileResource {
    public ShpkHeader Header;
    public ShaderEntry[] VertexShaderEntries = [];
    public ShaderEntry[] PixelShaderEntries = [];
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

    public override void LoadFile()
    {
        this.Header = this.Reader.ReadStructure<ShpkHeader>();
        if (this.Header.Magic != ShpkHeader.MagicValue)
            throw new InvalidDataException();
        this.VertexShaderEntries = Enumerable.Range(0, (int) this.Header.VertexShaderCount)
            .Select(_ => new ShaderEntry(this, ShaderType.Vertex)).ToArray();
        this.PixelShaderEntries = Enumerable.Range(0, (int) this.Header.PixelShaderCount)
            .Select(_ => new ShaderEntry(this, ShaderType.Pixel)).ToArray();

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

        this.Nodes = new ShaderNode[this.Header.NodeCount];
        for (var i = 0; i < this.Nodes.Length; i++) {
            this.Nodes[i].Id = this.Reader.ReadUInt32();
            var passCount = this.Reader.ReadUInt32();
            this.Nodes[i].PassIndices = this.Reader.ReadBytes(16);
            this.Nodes[i].SystemKeys = this.Reader.ReadStructuresAsArray<uint>(this.SystemKeys.Length);
            this.Nodes[i].SceneKeys = this.Reader.ReadStructuresAsArray<uint>(this.SceneKeys.Length);
            this.Nodes[i].MaterialKeys = this.Reader.ReadStructuresAsArray<uint>(this.MaterialKeys.Length);
            this.Nodes[i].SubViewKeys = this.Reader.ReadStructuresAsArray<uint>(this.SubViewKeys.Length);
            this.Nodes[i].Passes = this.Reader.ReadStructuresAsArray<ShaderNodePass>((int) passCount);
        }

        this.NodeAliases = this.Reader.ReadStructuresAsArray<ShaderNodeAlias>((int) this.Header.NodeAliasCount);
    }

    public class ShaderEntry : IShaderEntry {
        private readonly ShpkFile _file;

        public ShaderEntry(ShpkFile file, ShaderType shaderType)
        {
            this._file = file;
            this.Header = this._file.Reader.ReadStructure<ShaderHeader>();
            this.InputTables = this._file.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.NumInputs);
            this.InputNames = this.InputTables.Select(
                x => Encoding.UTF8.GetString(
                    this._file.Data,
                    (int) (this._file.Header.StringsOffset + x.InputStringOffset),
                    x.InputStringSize)).ToArray();
            this.ShaderType = shaderType;
        }

        public ShaderHeader Header { get; set; }
        public ShaderInput[] InputTables { get; set; }
        public string[] InputNames { get; set; }

        public ReadOnlySpan<byte> ByteCode =>
            this._file.DataSpan.Slice(
                (int) (this._file.Header.BlobOffset + this.Header.BlobOffset),
                (int) this.Header.BlobSize);

        public uint ByteCodeCrc32 => Crc32.Get(this.ByteCode);

        public ShaderType ShaderType { get; }

        public override string ToString() => this.Header.ToString();
    }
}
