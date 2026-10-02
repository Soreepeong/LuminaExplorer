using System;
using System.IO;
using System.Runtime.InteropServices;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>
/// Equipment VFX parameter file (chara/xls/equipmentParameter/equipmentVfxParameter.evp).
/// Used by equipment whose EQP entry has BodyUsesEvpTable/HeadUsesEvpTable set.
/// <code>
/// [Magic:3 bytes, "EVP"][NumModels:byte]
/// NumModels x [ModelSetId:ushort]            sorted
/// NumModels x [Flags:512 bytes]             one byte per Mount sheet row
/// </code>
/// </summary>
[FileExtension(".evp")]
public class EvpFile : FileResource {
    /// <summary>"EVP"; only the low 24 bits of the first uint are the magic, the high byte is the model count.</summary>
    public const uint MagicValue = 0x00505645;

    public const uint MagicMask = 0x00FFFFFF;
    public const int FlagArraySize = 512;

    public ushort[] ModelSetIds = null!;

    /// <summary>For each entry of <see cref="ModelSetIds"/>, <see cref="FlagArraySize"/> flags indexed by mount row ID.</summary>
    public EvpFlags[][] FlagArrays = null!;

    public override void LoadFile()
    {
        var header = this.Reader.ReadUInt32();
        if ((header & MagicMask) != MagicValue)
            throw new InvalidDataException();

        var count = (int) (header >> 24);
        if (this.Data.Length < 4 + count * (2 + FlagArraySize))
            throw new InvalidDataException("File is too small for the declared number of models.");

        var span = this.Data.AsSpan(4);
        this.ModelSetIds = MemoryMarshal.Cast<byte, ushort>(span[..(count * 2)]).ToArray();

        span = span[(count * 2)..];
        this.FlagArrays = new EvpFlags[count][];
        for (var i = 0; i < count; i++)
            this.FlagArrays[i] = MemoryMarshal.Cast<byte, EvpFlags>(span.Slice(i * FlagArraySize, FlagArraySize)).ToArray();
    }

    /// <summary>Gets the flag array of a model set ID; returns false if the model set ID is not listed.</summary>
    public bool TryGetFlags(ushort modelSetId, out EvpFlags[] flags)
    {
        var index = Array.IndexOf(this.ModelSetIds, modelSetId);
        if (index < 0) {
            flags = Array.Empty<EvpFlags>();
            return false;
        }

        flags = this.FlagArrays[index];
        return true;
    }

    /// <summary>Gets the flag of a model set ID for a mount row ID, or <see cref="EvpFlags.None"/> if not listed.</summary>
    public EvpFlags GetFlag(ushort modelSetId, int mountRowId) =>
        mountRowId is >= 0 and < FlagArraySize && this.TryGetFlags(modelSetId, out var flags)
            ? flags[mountRowId]
            : EvpFlags.None;

    /// <summary>Per-mount flags; per Penumbra, a set flag means the mount disables the effect for that slot.</summary>
    [Flags]
    public enum EvpFlags : byte {
        None = 0x00,
        Body = 0x01,
        Head = 0x02,
        Both = Body | Head,
    }
}
