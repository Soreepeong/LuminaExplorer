using System.Numerics;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

public interface ICamera {
    public Matrix4x4 View { get; }

    public Matrix4x4 Projection { get; }
}