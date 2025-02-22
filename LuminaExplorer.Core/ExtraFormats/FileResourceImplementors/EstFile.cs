using System;
using System.Linq;
using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".est")]
public class EstFile : FileResource {
    public uint[] RaceAndSetIds = null!;
    public ushort[] SkeletonIds = null!;

    public override void LoadFile()
    {
        var count = this.Reader.ReadInt32();
        this.RaceAndSetIds = this.Reader.ReadUInt32Array(count);
        this.SkeletonIds = this.Reader.ReadUInt16Array(count);
    }

    public ushort[] RaceIds => this.RaceAndSetIds.Select(x => unchecked((ushort) (x >> 16))).ToArray();

    public ushort[] SetIds => this.RaceAndSetIds.Select(x => unchecked((ushort) x)).ToArray();

    public ushort? GetSkeletonId(uint raceAndSetId)
    {
        var i = Array.BinarySearch(this.RaceAndSetIds, raceAndSetId);
        return i < 0 ? null : this.SkeletonIds[i];
    }

    public ushort? GetSkeletonId(int race, int setId) => this.GetSkeletonId((uint) (race << 16 | setId));
}
