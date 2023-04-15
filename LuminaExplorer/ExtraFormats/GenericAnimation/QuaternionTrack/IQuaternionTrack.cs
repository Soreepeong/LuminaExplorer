using System.Numerics;

namespace LuminaExplorer.ExtraFormats.GenericAnimation.QuaternionTrack;

public interface IQuaternionTrack : ITimeToQuantity {
    Quaternion Interpolate(float t);
}
