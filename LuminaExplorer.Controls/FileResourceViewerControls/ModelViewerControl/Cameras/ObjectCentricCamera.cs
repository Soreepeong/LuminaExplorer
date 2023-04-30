using System;
using System.Numerics;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

public class ObjectCentricCamera : ICamera {
    public readonly System3D System;

    private Vector3 _target = Vector3.Zero;
    private float _yawFromTarget;
    private float _pitchFromTarget;
    private float _distance = 1f;
    private Vector2 _viewport = Vector2.One;

    private Matrix4x4? _view;
    private Matrix4x4? _projection;

    public ObjectCentricCamera(System3D system) {
        System = system;
    }

    public ObjectCentricCamera(Vector3 bboxMin, Vector3 bboxMax, System3D system) {
        System = system;
        var target = (bboxMin + bboxMax) / 2;
        Update(
            target: target,
            yawFromTarget: MathF.PI,
            pitchFromTarget: 0,
            distance: 64);
    }

    public Vector3 Target {
        get => _target;
        set => Update(target: value);
    }

    public Vector2 Viewport {
        get => _viewport;
        set => Update(viewport: value);
    }

    public float YawFromTarget {
        get => _yawFromTarget;
        set => Update(yawFromTarget: value);
    }

    public float PitchFromTarget {
        get => _pitchFromTarget;
        set => Update(pitchFromTarget: value);
    }

    public float Distance {
        get => _distance;
        set => Update(distance: value);
    }

    public Matrix4x4 View {
        get {
            if (_view is null) {
                var rotation = Matrix4x4.CreateFromYawPitchRoll(-_yawFromTarget, _pitchFromTarget, 0);
                var scaledDistance = MathF.Pow(2, _distance / 64f);
                _view = Matrix4x4.CreateLookAt(
                    cameraPosition: Vector3.Transform(System.Forward * scaledDistance, rotation),
                    cameraTarget: _target,
                    cameraUpVector: System.Up);
            }

            return _view.Value;
        }
    }

    public Matrix4x4 Projection => _projection ??=
        Matrix4x4.CreatePerspectiveFieldOfView(0.9f, _viewport.X / _viewport.Y, 0.1f, 10000.0f);

    public void Update(
        Vector3? target = null,
        Vector2? viewport = null,
        float? yawFromTarget = null,
        float? pitchFromTarget = null,
        float? distance = null) {
        _target = target ?? _target;
        _viewport = viewport ?? _viewport;
        _yawFromTarget = (yawFromTarget ?? _yawFromTarget) % (2 * MathF.PI);
        _pitchFromTarget = pitchFromTarget ?? _pitchFromTarget;
        _distance = distance ?? _distance;

        _view = null;
        _projection = null;
    }
}