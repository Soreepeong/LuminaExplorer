using System;
using System.Linq;
using System.Text;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Extensions;
using Lumina.Models.Materials;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// <see cref="MtrlFile"/> with a parser that understands Dawntrail materials, based on Penumbra's MtrlFile.
/// </summary>
/// <remarks>
/// <para>Lumina (as of 7.7.1) assumes that the data set (color table and dye table) is at most
/// 512 + 32 bytes long, and reads the material header right after that. Dawntrail color tables are 2048 bytes long
/// (32 rows of 64 bytes), so Lumina reads the material header from the middle of the color table, and ends up with
/// no shader keys, constants or samplers. <see cref="Lumina.Models.Materials.Material"/> then fails with
/// <see cref="System.IndexOutOfRangeException"/>.</para>
/// <para>This parser skips the whole data set, as Penumbra does. <see cref="MtrlFile.ColorSetInfo"/> and
/// <see cref="MtrlFile.ColorSetDyeInfo"/> are read from the beginning of the data set as Lumina would.</para>
/// <para>This class is substituted for <see cref="MtrlFile"/> whenever a file resource is loaded through the virtual
/// file system.</para>
/// </remarks>
public class PenumbraMtrlFile : MtrlFile {
    /// <summary>Raw data set; the color table (if any) followed by the dye table (if any).</summary>
    public byte[] DataSet = [];

    /// <summary>Shader flags; 0x1 means that back faces are hidden, and 0x10 enables transparency.</summary>
    public uint ShaderFlags => this.MaterialHeader.Unknown1 | ((uint) this.MaterialHeader.Unknown2 << 16);

    /// <summary>Name of the shader package, such as character.shpk.</summary>
    public string ShaderPackageName => this.GetString(this.FileHeader.ShaderPackageNameOffset);

    public override unsafe void LoadFile()
    {
        this.FileHeader = MaterialFileHeader.Read(this.Reader);
        this.TextureOffsets = new TextureOffset[this.FileHeader.TextureCount];

        var offsets = this.Reader.ReadUInt32Array(this.FileHeader.TextureCount);
        for (var i = 0; i < offsets.Length; i++) {
            this.TextureOffsets[i].Offset = (ushort) offsets[i];
            this.TextureOffsets[i].Flags = (ushort) (offsets[i] >> 16);
        }

        this.UvColorSets = this.Reader.ReadStructuresAsArray<UvColorSet>(this.FileHeader.UvSetCount);
        this.ColorSets = this.Reader.ReadStructuresAsArray<ColorSet>(this.FileHeader.ColorSetCount);
        this.Strings = this.Reader.ReadBytes(this.FileHeader.StringTableSize);

        this.Reader.Seek(this.Reader.BaseStream.Position + this.FileHeader.AdditionalDataSize);

        var dataSetOffset = this.Reader.BaseStream.Position;
        if (this.FileHeader.DataSetSize >= sizeof(ColorSetInfo)) {
            this.ColorSetInfo = this.Reader.ReadStructure<ColorSetInfo>();
            if (this.FileHeader.DataSetSize >= sizeof(ColorSetInfo) + sizeof(ColorSetDyeInfo))
                this.ColorSetDyeInfo = this.Reader.ReadStructure<ColorSetDyeInfo>();
        }

        this.Reader.Seek(dataSetOffset);
        this.DataSet = this.Reader.ReadBytes(this.FileHeader.DataSetSize);
        this.Reader.Seek(dataSetOffset + this.FileHeader.DataSetSize);

        this.MaterialHeader = this.Reader.ReadStructure<MaterialHeader>();

        this.ShaderKeys = this.Reader.ReadStructuresAsArray<ShaderKey>(this.MaterialHeader.ShaderKeyCount);
        this.Constants = this.Reader.ReadStructuresAsArray<Constant>(this.MaterialHeader.ConstantCount);
        this.Samplers = this.Reader.ReadStructuresAsArray<Sampler>(this.MaterialHeader.SamplerCount);

        this.ShaderValues = this.Reader.ReadSingleArray(this.MaterialHeader.ShaderValueListSize / 4);
    }

    /// <summary>Creates a <see cref="Material"/> from the given file, skipping samplers unknown to Lumina.</summary>
    /// <remarks>
    /// <see cref="Material"/> (as of Lumina 7.7.1) makes a texture out of each of the first <i>n</i> samplers, where
    /// <i>n</i> is the number of textures, and throws if there are fewer samplers than textures, or if any of those
    /// samplers is not one of <see cref="TextureUsage"/>. Dawntrail materials use samplers that Lumina does not know
    /// of, such as g_SamplerIndex. The material is built with a texture for each sampler that Lumina knows of;
    /// <see cref="Material.File"/> is set to a copy of the file with only those samplers, so that
    /// <see cref="Material.Textures"/> and <see cref="MtrlFile.Samplers"/> stay in sync.
    /// </remarks>
    public static Material CreateMaterial(MtrlFile mtrlFile)
    {
        if (mtrlFile is not PenumbraMtrlFile pmf)
            return new(mtrlFile);

        var clone = (PenumbraMtrlFile) pmf.MemberwiseClone();
        clone.Samplers = pmf.Samplers
            .Where(x => x.TextureIndex < pmf.TextureOffsets.Length)
            .Where(x => Enum.IsDefined((TextureUsage) x.SamplerId))
            .ToArray();

        var material = new Material(pmf.FilePath?.Path ?? string.Empty);
        typeof(Material).GetProperty(nameof(Material.File))!.SetValue(material, clone);
        typeof(Material).GetProperty(nameof(Material.ShaderPack))!.SetValue(
            material,
            pmf.GetString(pmf.FileHeader.ShaderPackageNameOffset));
        typeof(Material).GetProperty(nameof(Material.Textures))!.SetValue(
            material,
            clone.Samplers
                .Select(
                    x => new Texture(
                        material,
                        (TextureUsage) x.SamplerId,
                        pmf.GetString(pmf.TextureOffsets[x.TextureIndex].Offset)))
                .ToArray());
        return material;
    }

    /// <summary>Gets the path of the texture at the given index of <see cref="MtrlFile.TextureOffsets"/>.</summary>
    public string GetTexturePath(int textureIndex) =>
        textureIndex < this.TextureOffsets.Length
            ? this.GetString(this.TextureOffsets[textureIndex].Offset)
            : string.Empty;

    private string GetString(int offset) =>
        offset < this.Strings.Length
            ? Encoding.UTF8.GetStringNullTerminated(this.Strings.AsSpan(offset))
            : string.Empty;
}
