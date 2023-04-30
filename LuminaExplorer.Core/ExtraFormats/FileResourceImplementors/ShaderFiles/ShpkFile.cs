using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shpk")]
public class ShpkFile : FileResource {
    public ShpkHeader Header;
    public ShaderEntry[] ShaderEntries = null!;
    public Hmm1[] Hmm1 = null!;
    public InputTable[] AllConstantInputTables = null!;
    public InputTable[] AllSamplerInputTables = null!;
    
    public override void LoadFile() {
        Header = Reader.ReadStructure<ShpkHeader>();
        if (Header.Magic != ShpkHeader.MagicValue)
            throw new InvalidDataException();
        ShaderEntries = Enumerable.Empty<ShaderEntry>()
            .Concat(Enumerable
                .Range(0, (int) Header.NumVertexShaders)
                .Select(_ => new ShaderEntry(this, ShaderType.Vertex)))
            .Concat(Enumerable
                .Range(0, (int) Header.NumPixelShaders)
                .Select(_ => new ShaderEntry(this, ShaderType.Pixel)))
            .ToArray();

        Hmm1 = Reader.ReadStructuresAsArray<Hmm1>((int) Header.NumHmm1);
        AllConstantInputTables = Reader.ReadStructuresAsArray<InputTable>((int)Header.NumConstantInputs);
        AllSamplerInputTables = Reader.ReadStructuresAsArray<InputTable>((int)Header.NumSamplerInputs);

        var ddx = Reader.Position;
        Reader.Position = 0x10998;
        var hmm3 = Reader.ReadStructuresAsArray<Hmm1>((0x1a118 - 0x10998) / 16);
        Reader.Position = ddx; 
        Debugger.Break();
    }

    struct Test2 {
        public InputId InputId;
        uint a2;
        uint a3;
        uint a4;
    }

    public class ShaderEntry : IShaderEntry {
        private readonly ShpkFile _file;

        public ShaderEntry(ShpkFile file, ShaderType shaderType) {
            _file = file;
            Header = _file.Reader.ReadStructure<ShaderHeader>();
            InputTables = _file.Reader.ReadStructuresAsArray<InputTable>(Header.NumInputs);
            InputNames = InputTables.Select(x => Encoding.UTF8.GetString(
                _file.Data,
                (int) (_file.Header.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
            ShaderType = shaderType;
        }
        
        public ShaderHeader Header { get; set; }
        public InputTable[] InputTables { get; set;}
        public string[] InputNames { get; set;}

        public ReadOnlySpan<byte> ByteCode => _file.DataSpan.Slice(
            (int) (_file.Header.ShaderBytecodeBlockOffset + Header.BytecodeOffset),
            (int) Header.BytecodeSize);

        public ShaderType ShaderType { get; }

        public override string ToString() => Header.ToString();
    }
}
