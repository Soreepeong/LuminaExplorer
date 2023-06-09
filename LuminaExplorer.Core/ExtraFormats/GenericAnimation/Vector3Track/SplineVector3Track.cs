using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

public class SplineVector3Track : IVector3Track {
    private readonly Nurbs _nurbs;
    private readonly int _numFrames;
    private readonly float _frameDuration;

    public SplineVector3Track(Nurbs nurbs, float duration, int numFrames, float frameDuration) {
        _nurbs = nurbs;
        Duration = duration;
        _numFrames = numFrames;
        _frameDuration = frameDuration;
    }

    public bool IsEmpty => false;

    public bool IsStatic => false;
    
    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() => Enumerable.Range(0, _numFrames).Select(x => x * _frameDuration);

    public Vector3 Interpolate(float t) {
        var v = _nurbs[t / _frameDuration];
        return new(v[0], v[1], v[2]);
    }

    public override string ToString() => $"SplineVector3Track({Duration:0.00}s)";
}