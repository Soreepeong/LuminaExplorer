using System.Numerics;

namespace LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;

public interface IQuaternionTrack : ITimeToQuantity {
    Quaternion Interpolate(float t);
}
