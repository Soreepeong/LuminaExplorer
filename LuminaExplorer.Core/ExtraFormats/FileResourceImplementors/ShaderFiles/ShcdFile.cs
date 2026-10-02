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
        if (this.FileHeader.Version >= ShcdVersion.V0501) {
            this.Header = this.Reader.ReadStructure<ShaderHeader>();
        } else {
            // Version 0x0301 has no UAV and texture counts.
            this.Header = new() {
                BlobOffset = this.Reader.ReadUInt32(),
                BlobSize = this.Reader.ReadUInt32(),
                ConstantCount = this.Reader.ReadUInt16(),
                SamplerCount = this.Reader.ReadUInt16(),
            };
        }

        if (this.FileHeader.Version >= ShcdVersion.V0601)
            this.Unk131 = this.Reader.ReadUInt32();
        this.InputTables = this.Reader.ReadStructuresAsArray<ShaderInput>(this.Header.NumInputs);
        this.InputNames = this.InputTables.Select(
            x => Encoding.UTF8.GetString(
                this.Data,
                (int) (this.FileHeader.InputStringBlockOffset + x.InputStringOffset),
                (int) x.InputStringSize)).ToArray();
    }

    public ShaderHeader Header { get; set; }

    /// <summary>Unknown value present since version 0x0601; same as <see cref="ShpkFile.ShaderEntry.Unk131"/>.</summary>
    public uint Unk131 { get; set; }

    public ShaderInput[] InputTables { get; set; } = null!;
    public string[] InputNames { get; set; } = null!;

    public ReadOnlySpan<byte> RawBlob =>
        this.DataSpan.Slice(
            (int) (this.FileHeader.ShaderBytecodeBlockOffset + this.Header.BlobOffset),
            (int) this.Header.BlobSize);

    /// <summary>Vertex shaders are prefixed with the declared and used vertex input masks, as in shpk.</summary>
    public int AdditionalHeaderSize => this.FileHeader.ShaderType == ShaderType.Vertex
        ? (int) Math.Min(this.FileHeader.DirectXVersion == DirectXVersion.Dx9 ? 4u : 8u, this.Header.BlobSize)
        : 0;

    public ReadOnlySpan<byte> AdditionalHeader => this.RawBlob[..this.AdditionalHeaderSize];

    public ReadOnlySpan<byte> ByteCode => this.RawBlob[this.AdditionalHeaderSize..];

    public uint ByteCodeCrc32 => Crc32.Get(this.ByteCode);

    public ShaderType ShaderType => this.FileHeader.ShaderType;
}
