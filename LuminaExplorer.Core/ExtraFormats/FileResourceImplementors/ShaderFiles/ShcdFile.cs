using System;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shcd")]
public class ShcdFile : FileResource, IShaderEntry {
    public ShcdHeader FileHeader;
    
    public override void LoadFile() {
        FileHeader = Reader.ReadStructure<ShcdHeader>();
        if (FileHeader.Magic != ShcdHeader.MagicValue)
            throw new InvalidDataException();
        Header = Reader.ReadStructure<ShaderHeader>();
        InputTables = Reader.ReadStructuresAsArray<InputTable>(Header.NumInputs);
        InputNames = InputTables.Select(x => Encoding.UTF8.GetString(
            Data,
            (int) (FileHeader.InputStringBlockOffset + x.InputStringOffset),
            (int) x.InputStringSize)).ToArray();
    }

    public ShaderHeader Header { get; set; }
    public InputTable[] InputTables { get; set; } = null!;
    public string[] InputNames { get; set; } = null!;

    public ReadOnlySpan<byte> ByteCode => DataSpan.Slice(
        (int) (FileHeader.ShaderBytecodeBlockOffset + Header.BytecodeOffset),
        (int) Header.BytecodeSize);

    public ShaderType ShaderType => FileHeader.ShaderType;
}