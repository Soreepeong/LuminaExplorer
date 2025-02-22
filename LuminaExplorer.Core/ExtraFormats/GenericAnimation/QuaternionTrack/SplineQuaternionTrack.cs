using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;

public class SplineQuaternionTrack : IQuaternionTrack {
    private readonly Nurbs _nurbs;
    private readonly int _numFrames;
    private readonly float _frameDuration;

    public SplineQuaternionTrack(Nurbs nurbs, float duration, int numFrames, float frameDuration)
    {
        this._nurbs = nurbs;
        this.Duration = duration;
        this._numFrames = numFrames;
        this._frameDuration = frameDuration;
    }

    public bool IsEmpty => false;

    public bool IsStatic => false;

    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() =>
        Enumerable.Range(0, this._numFrames).Select(x => x * this._frameDuration);

    public Quaternion Interpolate(float t)
    {
        var v = this._nurbs[t / this._frameDuration];
        return Quaternion.Normalize(new(v[0], v[1], v[2], v[3]));
    }

    public override string ToString() => $"SplineQuaternionTrack({this.Duration:0.00}s)";
}
