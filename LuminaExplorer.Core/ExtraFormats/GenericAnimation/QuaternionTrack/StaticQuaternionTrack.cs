using System.Collections.Generic;
using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;

public class StaticQuaternionTrack : IQuaternionTrack {
    private readonly Quaternion _value;

    public StaticQuaternionTrack(Quaternion value, float duration, bool isEmpty) {
        _value = value;
        IsEmpty = isEmpty;
        Duration = duration;
    }

    public bool IsEmpty { get; }

    public bool IsStatic => true;

    public float Duration { get; }

    public IEnumerable<float> GetFrameTimes() => new[] {0f};

    public Quaternion Interpolate(float t) => _value;

    public override string ToString() => IsEmpty 
        ? $"StaticQuaternionTrack({Duration:0.00}s): empty"
        : $"StaticQuaternionTrack({Duration:0.00}s): {_value}";
}
