using System.Collections.Generic;
using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;

public class StaticQuaternionTrack : IQuaternionTrack {
    private readonly Quaternion _value;

    public StaticQuaternionTrack(Quaternion value, float duration, bool isEmpty)
    {
        this._value = value;
        this.IsEmpty = isEmpty;
        this.Duration = duration;
    }

    public bool IsEmpty { get; }

    public bool IsStatic => true;

    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() => [0f];

    public Quaternion Interpolate(float t) => this._value;

    public override string ToString() =>
        this.IsEmpty
            ? $"StaticQuaternionTrack({this.Duration:0.00}s): empty"
            : $"StaticQuaternionTrack({this.Duration:0.00}s): {this._value}";
}
