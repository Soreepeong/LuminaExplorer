using System;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shpk")]
public class ShpkFile : FileResource {
    public ShpkHeader Header;
    public ShaderEntry[] VertexShaderEntries = null!;
    public ShaderEntry[] PixelShaderEntries = null!;
    public ShaderMaterialParam[] MaterialParams = null!;
    public ShaderInput[] Constants = null!;
    public ShaderInput[] Samplers = null!;
    public ShaderInput[] Uavs = null!;
    public ShaderKey[] SystemKeys = null!;
    public ShaderKey[] SceneKeys = null!;
    public ShaderKey[] MaterialKeys = null!;
    public ShaderKey[] SubViewKeys = null!;
    public ShaderNode[] Nodes = null!;
    public ShaderItem[] Items = null!;

    public override void LoadFile() {
        Header = Reader.ReadStructure<ShpkHeader>();
        if (Header.Magic != ShpkHeader.MagicValue)
            throw new InvalidDataException();
        VertexShaderEntries = Enumerable.Range(0, (int) Header.VertexShaderCount)
            .Select(_ => new ShaderEntry(this, ShaderType.Vertex)).ToArray();
        PixelShaderEntries = Enumerable.Range(0, (int) Header.PixelShaderCount)
            .Select(_ => new ShaderEntry(this, ShaderType.Pixel)).ToArray();

        MaterialParams = Reader.ReadStructuresAsArray<ShaderMaterialParam>((int) Header.MaterialParamCount);
        Constants = Reader.ReadStructuresAsArray<ShaderInput>((int) Header.ConstantCount);
        Samplers = Reader.ReadStructuresAsArray<ShaderInput>((int) Header.SamplerCount);
        Uavs = Reader.ReadStructuresAsArray<ShaderInput>((int) Header.UavCount);
        SystemKeys = Reader.ReadStructuresAsArray<ShaderKey>((int) Header.SystemKeyCount);
        SceneKeys = Reader.ReadStructuresAsArray<ShaderKey>((int) Header.SceneKeyCount);
        MaterialKeys = Reader.ReadStructuresAsArray<ShaderKey>((int) Header.MaterialKeyCount);
        SubViewKeys = new[] {
            new ShaderKey {Id = 1, DefaultValue = Reader.ReadUInt32()},
            new ShaderKey {Id = 2, DefaultValue = Reader.ReadUInt32()},
        };
        Nodes = new ShaderNode[Header.NodeCount];
        for (var i = 0; i < Nodes.Length; i++) {
            Nodes[i].Id = Reader.ReadUInt32();
            var passCount = Reader.ReadUInt32();
            Nodes[i].PassIndices = Reader.ReadBytes(16);
            Nodes[i].SystemKeys = Reader.ReadStructuresAsArray<uint>(SystemKeys.Length);
            Nodes[i].SceneKeys = Reader.ReadStructuresAsArray<uint>(SceneKeys.Length);
            Nodes[i].MaterialKeys = Reader.ReadStructuresAsArray<uint>(MaterialKeys.Length);
            Nodes[i].SubViewKeys = Reader.ReadStructuresAsArray<uint>(SubViewKeys.Length);
            Nodes[i].Passes = Reader.ReadStructuresAsArray<ShaderNodePass>((int) passCount);
        }

        Items = Reader.ReadStructuresAsArray<ShaderItem>((int) Header.ItemCount);
    }
    
    public class ShaderEntry : IShaderEntry {
        private readonly ShpkFile _file;

        public ShaderEntry(ShpkFile file, ShaderType shaderType) {
            _file = file;
            Header = _file.Reader.ReadStructure<ShaderHeader>();
            InputTables = _file.Reader.ReadStructuresAsArray<ShaderInput>(Header.NumInputs);
            InputNames = InputTables.Select(x => Encoding.UTF8.GetString(
                _file.Data,
                (int) (_file.Header.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
            ShaderType = shaderType;
        }

        public ShaderHeader Header { get; set; }
        public ShaderInput[] InputTables { get; set; }
        public string[] InputNames { get; set; }

        public ReadOnlySpan<byte> ByteCode => _file.DataSpan.Slice(
            (int) (_file.Header.ShaderBytecodeBlockOffset + Header.BytecodeOffset),
            (int) Header.BytecodeSize);

        public ShaderType ShaderType { get; }

        public override string ToString() => Header.ToString();
    }
}
