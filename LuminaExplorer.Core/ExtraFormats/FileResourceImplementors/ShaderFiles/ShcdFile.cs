using System;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shcd")]
public class ShcdFile : FileResource {
    public ShcdHeader Header;
    public ShaderHeader ShaderHeader;
    public InputTable[]? InputTables;
    public VertexShaderInputTable[]? VertexShaderInputTables;
    public string[] InputNames = null!;

    public override void LoadFile() {
        Header = Reader.ReadStructure<ShcdHeader>();
        if (Header.Magic != ShcdHeader.MagicValue)
            throw new InvalidDataException();
        ShaderHeader = Reader.ReadStructure<ShaderHeader>();
        if (Header.ShaderType == ShaderType.Vertex && false) {
            VertexShaderInputTables = Reader.ReadStructuresAsArray<VertexShaderInputTable>(ShaderHeader.NumInputs);
            InputNames = VertexShaderInputTables.Select(x => Encoding.UTF8.GetString(
                Data,
                (int) (Header.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
        } else {
            InputTables = Reader.ReadStructuresAsArray<InputTable>(ShaderHeader.NumInputs);
            InputNames = InputTables.Select(x => Encoding.UTF8.GetString(
                Data,
                (int) (Header.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
        }
    }

    public Span<byte> ByteCode => DataSpan.Slice(
        (int) (Header.ShaderBytecodeBlockOffset + ShaderHeader.BytecodeOffset),
        (int) ShaderHeader.BytecodeSize);
}