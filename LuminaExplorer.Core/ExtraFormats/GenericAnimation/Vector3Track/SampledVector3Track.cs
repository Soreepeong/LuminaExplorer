using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

/// <summary>A track defined by evenly spaced samples, linearly interpolated.</summary>
public class SampledVector3Track : IVector3Track {
    private readonly Vector3[] _samples;
    private readonly float _frameDuration;

    public SampledVector3Track(Vector3[] samples, float duration, float frameDuration)
    {
        if (samples.Length == 0)
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        this._samples = samples;
        this.Duration = duration;
        this._frameDuration = frameDuration;
    }

    public bool IsEmpty => false;

    public bool IsStatic => false;

    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() =>
        Enumerable.Range(0, this._samples.Length).Select(x => x * this._frameDuration);

    public Vector3 Interpolate(float t)
    {
        if (this._samples.Length == 1 || !(this._frameDuration > 0) || !float.IsFinite(t))
            return this._samples[0];

        var f = Math.Clamp(t / this._frameDuration, 0, this._samples.Length - 1);
        var i = Math.Min((int) f, this._samples.Length - 2);
        return Vector3.Lerp(this._samples[i], this._samples[i + 1], f - i);
    }

    public override string ToString() => $"SampledVector3Track({this.Duration:0.00}s; {this._samples.Length} samples)";
}
