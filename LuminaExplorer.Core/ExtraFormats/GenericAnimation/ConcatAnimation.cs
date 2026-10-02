using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation;

public class ConcatAnimation : IAnimation {
    public readonly ImmutableList<IAnimation> Parts;
    private readonly ImmutableDictionary<int, ConcatTranslationTrack> _concatTranslationTracks;
    private readonly ImmutableDictionary<int, ConcatRotationTrack> _concatRotationTracks;
    private readonly ImmutableDictionary<int, ConcatScaleTrack> _concatScaleTracks;

    public ConcatAnimation(IEnumerable<IAnimation> animations)
    {
        this.Parts = animations.ToImmutableList();
        this.AffectedBoneIndices = this.Parts.SelectMany(x => x.AffectedBoneIndices).ToImmutableSortedSet();
        this._concatTranslationTracks = this.AffectedBoneIndices.ToImmutableDictionary(
            x => x,
            x => new ConcatTranslationTrack(this, x));
        this._concatRotationTracks = this.AffectedBoneIndices.ToImmutableDictionary(
            x => x,
            x => new ConcatRotationTrack(this, x));
        this._concatScaleTracks = this.AffectedBoneIndices.ToImmutableDictionary(
            x => x,
            x => new ConcatScaleTrack(this, x));
    }

    public float Duration => this.Parts.Select(x => x.Duration).Sum();

    public ImmutableSortedSet<int> AffectedBoneIndices { get; }

    public IVector3Track Translation(int boneIndex) => this._concatTranslationTracks[boneIndex];

    public IQuaternionTrack Rotation(int boneIndex) => this._concatRotationTracks[boneIndex];

    public IVector3Track Scale(int boneIndex) => this._concatScaleTracks[boneIndex];

    public override string ToString() =>
        $"ConcatAnimation({this.Parts.Count} parts; {this.Duration:0.00}s; {this.AffectedBoneIndices.Count} bones)";

    private class ConcatTranslationTrack : IVector3Track {
        private readonly ConcatAnimation _parent;
        private readonly IVector3Track[] _parts;

        internal ConcatTranslationTrack(ConcatAnimation parent, int boneIndex)
        {
            this._parent = parent;
            this._parts = this._parent.Parts.Select(x => x.Translation(boneIndex)).ToArray();
        }

        public bool IsEmpty => this._parts.All(x => x.IsEmpty);

        public bool IsStatic =>
            this._parts.All(x => x.IsStatic) && this._parts.Zip(this._parts.Skip(1))
                .All((x) => x.First.Interpolate(0) == x.Second.Interpolate(0));

        public float Duration => this._parent.Duration;

        public IEnumerable<float> GetFrameTimes() => ConcatFrameTimes(this._parts);

        public Vector3 Interpolate(float t)
        {
            // Avoid looping forever when there is nothing to advance through.
            if (!(this._parent.Duration > 0) || !float.IsFinite(t))
                return this._parts[0].Interpolate(0);

            t %= this._parent.Duration;
            while (true) {
                for (var i = 0; i < this._parts.Length; i++) {
                    var d = this._parent.Parts[i].Duration;
                    if (t < d)
                        return this._parts[i].Interpolate(t);
                    t -= d;
                }
            }
        }
    }

    private class ConcatRotationTrack : IQuaternionTrack {
        private readonly ConcatAnimation _parent;
        private readonly IQuaternionTrack[] _parts;

        internal ConcatRotationTrack(ConcatAnimation parent, int boneIndex)
        {
            this._parent = parent;
            this._parts = this._parent.Parts.Select(x => x.Rotation(boneIndex)).ToArray();
        }

        public bool IsEmpty => this._parts.All(x => x.IsEmpty);

        public bool IsStatic =>
            this._parts.All(x => x.IsStatic) && this._parts.Zip(this._parts.Skip(1))
                .All((x) => x.First.Interpolate(0) == x.Second.Interpolate(0));

        public float Duration => this._parent.Duration;

        public IEnumerable<float> GetFrameTimes() => ConcatFrameTimes(this._parts);

        public Quaternion Interpolate(float t)
        {
            // Avoid looping forever when there is nothing to advance through.
            if (!(this._parent.Duration > 0) || !float.IsFinite(t))
                return this._parts[0].Interpolate(0);

            t %= this._parent.Duration;
            while (true) {
                for (var i = 0; i < this._parts.Length; i++) {
                    var d = this._parent.Parts[i].Duration;
                    if (t < d)
                        return this._parts[i].Interpolate(t);
                    t -= d;
                }
            }
        }
    }

    private class ConcatScaleTrack : IVector3Track {
        private readonly ConcatAnimation _parent;
        private readonly IVector3Track[] _parts;

        internal ConcatScaleTrack(ConcatAnimation parent, int boneIndex)
        {
            this._parent = parent;
            this._parts = this._parent.Parts.Select(x => x.Scale(boneIndex)).ToArray();
        }

        public bool IsEmpty => this._parts.All(x => x.IsEmpty);

        public bool IsStatic =>
            this._parts.All(x => x.IsStatic) && this._parts.Zip(this._parts.Skip(1))
                .All(x => x.First.Interpolate(0) == x.Second.Interpolate(0));

        public float Duration => this._parent.Duration;

        public IEnumerable<float> GetFrameTimes() => ConcatFrameTimes(this._parts);

        public Vector3 Interpolate(float t)
        {
            // Avoid looping forever when there is nothing to advance through.
            if (!(this._parent.Duration > 0) || !float.IsFinite(t))
                return this._parts[0].Interpolate(0);

            t %= this._parent.Duration;
            while (true) {
                for (var i = 0; i < this._parts.Length; i++) {
                    var d = this._parent.Parts[i].Duration;
                    if (t < d)
                        return this._parts[i].Interpolate(t);
                    t -= d;
                }
            }
        }
    }

    private static IEnumerable<float> ConcatFrameTimes(IEnumerable<ITimeToQuantity> items)
    {
        var res = Enumerable.Repeat(0f, 0);
        var baseTime = 0f;
        foreach (var s in items) {
            var baseTimeCopy = baseTime;
            res = res.Concat(s.GetFrameTimes().Select(x => baseTimeCopy + x));
            baseTime += s.Duration;
        }

        return res;
    }
}
