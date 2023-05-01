using System;
using System.Numerics;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Cameras;

public class ObjectCentricCamera : ICamera {
    public readonly System3D System;

    private Vector3 _targetOffset = Vector3.Zero;
    private Vector3 _targetBboxMin = Vector3.Zero;
    private Vector3 _targetBboxMax = Vector3.Zero;
    private Vector2 _viewport = Vector2.One;
    private float _yaw;
    private float _pitch;
    private float _roll;
    private float _distanceExponent = 1f;
    private float _fovExponent;

    private Matrix4x4? _view;
    private Matrix4x4? _projection;

    public ObjectCentricCamera(System3D system) {
        System = system;
    }

    public ObjectCentricCamera(Vector3 bboxMin, Vector3 bboxMax, System3D system) {
        System = system;
        Update(
            targetOffset: Vector3.Zero,
            targetBboxMin: bboxMin,
            targetBboxMax: bboxMax,
            yaw: MathF.PI,
            pitch: 0,
            distanceExponent: 64);
    }

    public Vector3 TargetOffset {
        get => _targetOffset;
        set => Update(targetOffset: value);
    }

    public Vector3 TargetBboxMin {
        get => _targetBboxMin;
        set => Update(targetBboxMin: value);
    }

    public Vector3 TargetBboxMax {
        get => _targetBboxMax;
        set => Update(targetBboxMax: value);
    }

    public Vector2 Viewport {
        get => _viewport;
        set => Update(viewport: value);
    }

    public float Yaw {
        get => _yaw;
        set => Update(yaw: value);
    }

    public float Pitch {
        get => _pitch;
        set => Update(pitch: value);
    }

    public float Roll {
        get => _roll;
        set => Update(roll: value);
    }

    public float DistanceExponent {
        get => _distanceExponent;
        set => Update(distanceExponent: value);
    }

    public float FovExponent {
        get => _fovExponent;
        set => Update(fovExponent: value);
    }

    public float ScaledDistance => MathF.Pow(2, _distanceExponent / 64f);

    public Matrix4x4 RotationMatrix =>
        Matrix4x4.CreateRotationX(_pitch) * Matrix4x4.CreateRotationY(-_yaw);

    public bool IsUpsideDown => _pitch is >= MathF.PI / 2 and <= MathF.PI * 3 / 2;

    public Matrix4x4 View {
        get {
            _view ??=
                Matrix4x4.CreateTranslation((_targetBboxMin + _targetBboxMax) / -2) *
                Matrix4x4.CreateLookAt(
                    cameraPosition: Vector3.Transform(System.Forward * ScaledDistance, RotationMatrix),
                    cameraTarget: Vector3.Zero,
                    cameraUpVector: IsUpsideDown ? -System.Up : System.Up) *
                Matrix4x4.CreateRotationZ(_roll) *
                Matrix4x4.CreateTranslation(_targetOffset);

            return _view.Value;
        }
    }

    public Matrix4x4 Projection => _projection ??= _viewport.X <= 0 || _viewport.Y <= 0
        ? Matrix4x4.Identity
        : Matrix4x4.CreatePerspectiveFieldOfView(MathF.Pow(10, _fovExponent) / 10, _viewport.X / _viewport.Y, 0.1f,
            10000.0f);

    public void Update(
        Vector3? targetOffset = null,
        Vector3? targetBboxMin = null,
        Vector3? targetBboxMax = null,
        Vector2? viewport = null,
        float? yaw = null,
        float? pitch = null,
        float? roll = null,
        float? distanceExponent = null,
        float? fovExponent = null,
        bool resetDistance = false) {
        _targetOffset = targetOffset ?? _targetOffset;
        _targetBboxMin = targetBboxMin ?? _targetBboxMin;
        _targetBboxMax = targetBboxMax ?? _targetBboxMax;
        _viewport = viewport ?? _viewport;
        _yaw = MiscUtils.PositiveMod(yaw ?? _yaw, MathF.PI * 2);
        _pitch = MiscUtils.PositiveMod(pitch ?? _pitch, MathF.PI * 2);
        _roll = MiscUtils.PositiveMod(roll ?? _roll, MathF.PI * 2);
        _fovExponent = fovExponent ?? _fovExponent;
        _distanceExponent = distanceExponent ?? _distanceExponent;
        if (resetDistance) {
            _distanceExponent = MathF.Log2(Vector3.Dot(
                Vector3.Abs(_targetBboxMax - _targetBboxMin) / 2,
                Vector3.Abs(System.Up)) * 32) * 64;
        }

        _view = null;
        _projection = null;
    }
}
