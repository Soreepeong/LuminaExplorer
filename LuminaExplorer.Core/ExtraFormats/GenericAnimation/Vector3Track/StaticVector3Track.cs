using System.Collections.Generic;
using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

public class StaticVector3Track : IVector3Track {
    private readonly Vector3 _value;

    public StaticVector3Track(Vector3 value, float duration, bool isEmpty)
    {
        this._value = value;
        this.IsEmpty = isEmpty;
        this.Duration = duration;
    }

    public bool IsEmpty { get; }

    public bool IsStatic => true;

    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() => [0f];

    public Vector3 Interpolate(float t) => this._value;

    public override string ToString() =>
        this.IsEmpty
            ? $"StaticVector3Track({this.Duration:0.00}s): empty"
            : $"StaticVector3Track({this.Duration:0.00}s): {this._value}";
}
