using System.Linq;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation;

public class AnimationTrack {
    public readonly IVector3Track Translate;
    public readonly IQuaternionTrack Rotate;
    public readonly IVector3Track Scale;

    public AnimationTrack(IVector3Track translate, IQuaternionTrack rotate, IVector3Track scale)
    {
        this.Translate = translate;
        this.Rotate = rotate;
        this.Scale = scale;
    }

    public bool IsEmpty => this.Translate.IsEmpty && this.Rotate.IsEmpty && this.Scale.IsEmpty;

    public override string ToString() => string.Join(
        "; ",
        new[] {
            "AnimationTrack",
            this.Translate.IsEmpty ? "" : this.Translate is SplineVector3Track ? "T: spline" : "T: static",
            this.Rotate.IsEmpty ? "" : this.Rotate is SplineQuaternionTrack ? "R: spline" : "R: static",
            this.Scale.IsEmpty ? "" : this.Scale is SplineVector3Track ? "S: spline" : "S: static",
        }.Where(x => x != ""));
}
