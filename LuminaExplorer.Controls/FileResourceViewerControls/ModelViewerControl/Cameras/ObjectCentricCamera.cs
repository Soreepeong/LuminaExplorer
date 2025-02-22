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

    public ObjectCentricCamera(System3D system)
    {
        this.System = system;
    }

    public ObjectCentricCamera(Vector3 bboxMin, Vector3 bboxMax, System3D system)
    {
        this.System = system;
        this.Update(
            targetOffset: Vector3.Zero,
            targetBboxMin: bboxMin,
            targetBboxMax: bboxMax,
            yaw: MathF.PI,
            pitch: 0,
            distanceExponent: 64);
    }

    public Vector3 TargetOffset {
        get => this._targetOffset;
        set => this.Update(targetOffset: value);
    }

    public Vector3 TargetBboxMin {
        get => this._targetBboxMin;
        set => this.Update(targetBboxMin: value);
    }

    public Vector3 TargetBboxMax {
        get => this._targetBboxMax;
        set => this.Update(targetBboxMax: value);
    }

    public Vector2 Viewport {
        get => this._viewport;
        set => this.Update(viewport: value);
    }

    public float Yaw {
        get => this._yaw;
        set => this.Update(yaw: value);
    }

    public float Pitch {
        get => this._pitch;
        set => this.Update(pitch: value);
    }

    public float Roll {
        get => this._roll;
        set => this.Update(roll: value);
    }

    public float DistanceExponent {
        get => this._distanceExponent;
        set => this.Update(distanceExponent: value);
    }

    public float FovExponent {
        get => this._fovExponent;
        set => this.Update(fovExponent: value);
    }

    public float ScaledDistance => MathF.Pow(2, this._distanceExponent / 64f);

    public Matrix4x4 RotationMatrix =>
        Matrix4x4.CreateRotationX(this._pitch) * Matrix4x4.CreateRotationY(-this._yaw);

    public bool IsUpsideDown => this._pitch is >= MathF.PI / 2 and <= MathF.PI * 3 / 2;

    public Matrix4x4 View {
        get {
            this._view ??=
                Matrix4x4.CreateTranslation((this._targetBboxMin + this._targetBboxMax) / -2) *
                Matrix4x4.CreateLookAt(
                    cameraPosition: Vector3.Transform(this.System.Forward * this.ScaledDistance, this.RotationMatrix),
                    cameraTarget: Vector3.Zero,
                    cameraUpVector: this.IsUpsideDown ? -this.System.Up : this.System.Up) *
                Matrix4x4.CreateRotationZ(this._roll) *
                Matrix4x4.CreateTranslation(this._targetOffset);

            return this._view.Value;
        }
    }

    public Matrix4x4 Projection =>
        this._projection ??= this._viewport.X <= 0 || this._viewport.Y <= 0
            ? Matrix4x4.Identity
            : Matrix4x4.CreatePerspectiveFieldOfView(
                MathF.Pow(10, this._fovExponent) / 10,
                this._viewport.X / this._viewport.Y,
                0.1f,
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
        bool resetDistance = false)
    {
        this._targetOffset = targetOffset ?? this._targetOffset;
        this._targetBboxMin = targetBboxMin ?? this._targetBboxMin;
        this._targetBboxMax = targetBboxMax ?? this._targetBboxMax;
        this._viewport = viewport ?? this._viewport;
        this._yaw = MiscUtils.PositiveMod(yaw ?? this._yaw, MathF.PI * 2);
        this._pitch = MiscUtils.PositiveMod(pitch ?? this._pitch, MathF.PI * 2);
        this._roll = MiscUtils.PositiveMod(roll ?? this._roll, MathF.PI * 2);
        this._fovExponent = fovExponent ?? this._fovExponent;
        this._distanceExponent = distanceExponent ?? this._distanceExponent;
        if (resetDistance) {
            this._distanceExponent = MathF.Log2(
                Vector3.Dot(
                    Vector3.Abs(this._targetBboxMax - this._targetBboxMin) / 2,
                    Vector3.Abs(this.System.Up)) * 32) * 64;
        }

        this._view = null;
        this._projection = null;
    }
}
