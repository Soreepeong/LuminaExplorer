using Lumina.Data;
using Lumina.Data.Attributes;

namespace LuminaExplorer.ExtraFormats.FileResourceImplementors;

[FileExtension(".est")]
public class EstFile : FileResource {
    public uint[] RaceAndSetIds = null!;
    public ushort[] SkeletonIds = null!;

    public override void LoadFile() {
        var count = Reader.ReadInt32();
        RaceAndSetIds = Reader.ReadUInt32Array(count);
        SkeletonIds = Reader.ReadUInt16Array(count);
    }

    public ushort[] RaceIds => RaceAndSetIds.Select(x => unchecked((ushort) (x >> 16))).ToArray();
    
    public ushort[] SetIds => RaceAndSetIds.Select(x => unchecked((ushort) x)).ToArray();

    public ushort? GetSkeletonId(uint raceAndSetId) {
        var i = Array.BinarySearch(RaceAndSetIds, raceAndSetId);
        return i < 0 ? null : SkeletonIds[i];
    }

    public ushort? GetSkeletonId(int race, int setId) => GetSkeletonId((uint)(race << 16 | setId));
}
