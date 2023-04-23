using System.Linq;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation;

public class AnimationTrack {
    public readonly IVector3Track Translate;
    public readonly IQuaternionTrack Rotate;
    public readonly IVector3Track Scale;

    public AnimationTrack(IVector3Track translate, IQuaternionTrack rotate, IVector3Track scale) {
        Translate = translate;
        Rotate = rotate;
        Scale = scale;
    }

    public bool IsEmpty => Translate.IsEmpty && Rotate.IsEmpty && Scale.IsEmpty;

    public override string ToString() => string.Join("; ", new[] {
            "AnimationTrack",
            Translate.IsEmpty ? "" : Translate is SplineVector3Track ? "T: spline" : "T: static",
            Rotate.IsEmpty ? "" : Rotate is SplineQuaternionTrack ? "R: spline" : "R: static",
            Scale.IsEmpty ? "" : Scale is SplineVector3Track ? "S: spline" : "S: static",
        }.Where(x => x != ""));
}
