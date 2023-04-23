using System.Collections.Immutable;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation; 

public class AnimationBlock : IAnimation{
    public readonly ImmutableList<AnimationTrack> Tracks;
    public readonly ImmutableDictionary<int, int> BoneToTrack;

    public AnimationBlock(float duration, ImmutableList<AnimationTrack> tracks, ImmutableList<int> transformTrackToBoneIndices) {
        Duration = duration;
        Tracks = tracks;
        BoneToTrack = transformTrackToBoneIndices.Select((v, i) => (v, i)).ToImmutableDictionary(x => x.v, x => x.i);
    }

    public float Duration { get; }

    public ImmutableSortedSet<int> AffectedBoneIndices => BoneToTrack.Keys.ToImmutableSortedSet();
    
    public IVector3Track Translation(int boneIndex) => Tracks[BoneToTrack[boneIndex]].Translate;
    
    public IQuaternionTrack Rotation(int boneIndex) => Tracks[BoneToTrack[boneIndex]].Rotate;
    
    public IVector3Track Scale(int boneIndex) => Tracks[BoneToTrack[boneIndex]].Scale;

    public override string ToString() => $"{nameof(AnimationBlock)} ({Duration:0.00}s)";
}
