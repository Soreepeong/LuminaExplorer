using System.Numerics;

namespace LuminaExplorer.ExtraFormats.GenericAnimation.Vector3Track;

public interface IVector3Track : ITimeToQuantity {
    Vector3 Interpolate(float t);
}
