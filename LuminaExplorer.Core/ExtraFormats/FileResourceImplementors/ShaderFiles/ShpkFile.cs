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
    public ShaderEntry[] ShaderEntries = null!;

    public override void LoadFile() {
        Header = Reader.ReadStructure<ShpkHeader>();
        if (Header.Magic != ShpkHeader.MagicValue)
            throw new InvalidDataException();
        ShaderEntries = Enumerable
            .Range(0, (int) (Header.NumVertexShaders + Header.NumPixelShaders))
            .Select(_ => new ShaderEntry(this))
            .ToArray();
    }

    public class ShaderEntry {
        private readonly ShpkFile _file;
        public ShaderHeader ShaderHeader;
        public InputTable[]? InputTables;
        public string[] InputNames;

        public ShaderEntry(ShpkFile file) {
            _file = file;
            ShaderHeader = _file.Reader.ReadStructure<ShaderHeader>();
            InputTables = _file.Reader.ReadStructuresAsArray<InputTable>(ShaderHeader.NumInputs);
            InputNames = InputTables.Select(x => Encoding.UTF8.GetString(
                _file.Data,
                (int) (_file.Header.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
        }

        public Span<byte> ByteCode => _file.DataSpan.Slice(
            (int) (_file.Header.ShaderBytecodeBlockOffset + ShaderHeader.BytecodeOffset),
            (int) ShaderHeader.BytecodeSize);

        public override string ToString() => ShaderHeader.ToString();
    }
}
