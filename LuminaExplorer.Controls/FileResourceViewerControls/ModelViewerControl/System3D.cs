using System.Numerics;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;

public readonly struct System3D {
    public readonly Vector3 Forward;
    public readonly Vector3 Up;
    public readonly Vector3 Right;

    public System3D(Vector3 forward, Vector3 up, Vector3 right) {
        Forward = forward;
        Up = up;
        Right = right;
    }
}