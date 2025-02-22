using System;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using Lumina.Misc;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

[FileExtension(".shcd")]
public class ShcdFile : FileResource, IShaderEntry {
    public ShcdHeader FileHeader;

    public override void LoadFile()
    {
        this.FileHeader = this.Reader.ReadStructure<ShcdHeader>();
        if (this.FileHeader.Magic != ShcdHeader.MagicValue)
            throw new InvalidDataException();
        this.Header = this.Reader.ReadStructure<ShaderHeader>();
        this.InputTables = this.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.NumInputs);
        this.InputNames = this.InputTables.Select(
            x => Encoding.UTF8.GetString(
                this.Data,
                (int) (this.FileHeader.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
    }

    public ShaderHeader Header { get; set; }
    public ShaderInput[] InputTables { get; set; } = null!;
    public string[] InputNames { get; set; } = null!;

    public ReadOnlySpan<byte> ByteCode =>
        this.DataSpan.Slice(
            (int) (this.FileHeader.ShaderBytecodeBlockOffset + this.Header.BlobOffset),
            (int) this.Header.BlobSize);

    public uint ByteCodeCrc32 => Crc32.Get(this.ByteCode);

    public ShaderType ShaderType => this.FileHeader.ShaderType;
}
