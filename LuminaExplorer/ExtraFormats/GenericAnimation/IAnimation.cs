using System.Collections.Immutable;
using LuminaExplorer.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.ExtraFormats.GenericAnimation;

public interface IAnimation {
    float Duration { get; }

    ImmutableSortedSet<int> AffectedBoneIndices { get; }

    IVector3Track Translation(int boneIndex);

    IQuaternionTrack Rotation(int boneIndex);

    IVector3Track Scale(int boneIndex);
}
