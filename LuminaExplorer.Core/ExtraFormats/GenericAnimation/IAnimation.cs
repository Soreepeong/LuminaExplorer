using System.Collections.Immutable;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation;

public interface IAnimation {
    float Duration { get; }

    ImmutableSortedSet<int> AffectedBoneIndices { get; }

    IVector3Track Translation(int boneIndex);

    IQuaternionTrack Rotation(int boneIndex);

    IVector3Track Scale(int boneIndex);
}
