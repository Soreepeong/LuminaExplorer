using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.AtchStructs;

/// <summary>Placement of an attachment point for one state.</summary>
public struct AtchEntry {
    /// <summary>Maximum length of the bone name, in UTF-8 bytes, accepted by the game (as known by Penumbra).</summary>
    public const int MaxBoneNameLength = 34;

    /// <summary>Size of a single entry in the file, in bytes.</summary>
    public const int Size = 32;

    public string Bone;
    public float Scale;
    public Vector3 Offset;
    public Vector3 Rotation;

    public AtchEntry(string bone, float scale, Vector3 offset, Vector3 rotation)
    {
        this.Bone = bone;
        this.Scale = scale;
        this.Offset = offset;
        this.Rotation = rotation;
    }

    public override string ToString() =>
        $"{this.Bone} (Scale={this.Scale}, Offset={this.Offset}, Rotation={this.Rotation})";
}
