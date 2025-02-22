using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public unsafe class CustomMdlRenderer : BaseMdlRenderer {
    private readonly LruCache<string, SklbFile> _sklbCache = new(128, true);

    private CustomMdlRendererShader _shader;
    private ConstantBufferResource<CameraParameter> _paramCamera;
    private ConstantBufferResource<WorldViewMatrix> _paramWorldViewMatrix;
    private ConstantBufferResource<CustomMdlRendererShader.WorldMisc> _paramWorldMisc;
    private ConstantBufferResource<CustomMdlRendererShader.LightParameters> _paramLight;
    private Task<MdlFile>? _mdlTask;
    private Task<SklbFile[]>? _sklbTask;
    private Task<IAnimation>[]? _animationTasks;
    private Task<CustomMdlRendererShader.ModelObject>? _modelObject;

    private ResultDisposingTask<AnimatingJointsConstantBufferResource>? _animator;

    public CustomMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null)
    { }

    public CustomMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext)
    {
        this._shader = new(this.Device, this.DeviceContext);
        this._paramCamera = new(this.Device, this.DeviceContext);
        this._paramCamera.DataPull += this.ParamCameraOnDataPull;
        this._paramWorldViewMatrix = new(this.Device, this.DeviceContext);
        this._paramWorldViewMatrix.DataPull += this.ParamWorldViewMatrixOnDataPull;
        this._paramWorldMisc = new(this.Device, this.DeviceContext);
        this._paramWorldMisc.DataPull += this.ParamWorldMiscOnDataPull;
        this._paramLight = new(this.Device, this.DeviceContext, false, CustomMdlRendererShader.LightParameters.Default);
        this.Control.ViewportChanged += (_, _) => this.ResetCamera();
        this.Control.AnimationSpeedChanged += (_, _) => this.UpdateAnimationSpeed();
        this.Control.AnimationPlayingChanged += (_, _) => this.UpdateAnimationSpeed();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this.ModelTask = null;
            _ = SafeDispose.OneAsync(ref this._shader!);
            _ = SafeDispose.OneAsync(ref this._paramCamera!);
            _ = SafeDispose.OneAsync(ref this._paramWorldViewMatrix!);
            _ = SafeDispose.OneAsync(ref this._paramWorldMisc!);
            _ = SafeDispose.OneAsync(ref this._paramLight!);
        }

        base.Dispose(disposing);
    }

    public override Task<MdlFile>? ModelTask {
        get => this._mdlTask;
        set {
            if (value == this._mdlTask)
                return;

            void ClearModel()
            {
                this._mdlTask = null;
                this._modelObject?.ContinueWith(
                    r => {
                        if (r.IsCompletedSuccessfully) {
                            var modelObject = r.Result;
                            modelObject.DdsFileRequested -= this.ModelObjectOnDdsFileRequested;
                            modelObject.TextureLoadStateChanged -= this.ModelObjectOnLoadStateChanged;
                            modelObject.MtrlFileRequested -= this.ModelObjectOnMtrlFileRequested;
                        }
                    });
                this._modelObject = null;
                _ = SafeDispose.OneAsync(ref this._animator);
            }

            if (value is null) {
                ClearModel();
            } else {
                var prevTask = this._mdlTask;
                this._mdlTask = value;

                value.ContinueWith(
                    r => {
                        if (this._mdlTask != value)
                            return;
                        if (prevTask?.IsCompletedSuccessfully is true && r.Result.FilePath == prevTask.Result.FilePath)
                            return;

                        ClearModel();
                        this._mdlTask = value;

                        this._modelObject = Task.Run(
                            () => {
                                var modelObject = new CustomMdlRendererShader.ModelObject(this._shader, r.Result);
                                modelObject.DdsFileRequested += this.ModelObjectOnDdsFileRequested;
                                modelObject.TextureLoadStateChanged += this.ModelObjectOnLoadStateChanged;
                                modelObject.MtrlFileRequested += this.ModelObjectOnMtrlFileRequested;
                                return modelObject;
                            });

                        this._sklbTask = Task
                            .WhenAll(
                                this._modelObject,
                                this.Control.ModelInfoResolverTask ??= ModelInfoResolver.GetResolver(
                                    this.Control.GetTypedFileAsync<EstFile>,
                                    this.Control.GetTypedFileAsync<PbdFile>))
                            .ContinueWith(
                                _ => {
                                    if (this._mdlTask != value)
                                        throw new OperationCanceledException();
                                    return Task.WhenAll(
                                        this.Control.ModelInfoResolverTask.Result
                                            .FindSklbPath(value.Result.FilePath.Path)
                                            .Select(
                                                x => this._sklbCache.TryGet(x, out var sklb)
                                                    ? Task.FromResult(Tuple.Create(x, (SklbFile?) sklb))
                                                    : this.Control.GetTypedFileAsync<SklbFile>(x)
                                                        .ContinueWith(r2 => Tuple.Create(x, r2.Result))));
                                }).Unwrap().ContinueWith(
                                r2 => {
                                    if (r2.IsFaulted)
                                        throw r2.Exception!;
                                    if (!r2.IsCompletedSuccessfully || r2.Result is null)
                                        throw new("No associated skeleton file found");
                                    foreach (var (sklbPath, sklb) in r2.Result)
                                        if (sklb is not null)
                                            this._sklbCache.Add(sklbPath, sklb);

                                    this.LoadAnimationIfPossible();
                                    return r2.Result
                                        .Select(x => x.Item2)
                                        .Where(x => x is not null)
                                        .Select(x => x!)
                                        .ToArray();
                                });

                        this.Control.RunOnUiThreadAfter(
                            this._modelObject,
                            r2 => {
                                if (this._mdlTask != value || !r2.IsCompletedSuccessfully)
                                    return;

                                this.ResetCamera(this._mdlTask.Result.ModelBoundingBoxes);
                            });
                    });
            }
        }
    }

    public override Task<SklbFile[]>? SkeletonTask => this._sklbTask;

    public override Task<IAnimation>[]? AnimationsTask {
        get => this._animationTasks;
        set {
            if (this._animationTasks == value)
                return;

            this._animationTasks = value;
            this.LoadAnimationIfPossible();
        }
    }

    private void LoadAnimationIfPossible()
    {
        var mdlTask = this._mdlTask;
        var sklbTask = this._sklbTask;
        if (mdlTask is not null && sklbTask is not null) {
            var animator = this._animator;
            if (animator is null) {
                this._animator = animator = new(
                    Task.Run(
                        () => new AnimatingJointsConstantBufferResource(
                            this.Device,
                            this.DeviceContext,
                            mdlTask.Result,
                            sklbTask.Result)));
                this.Control.RunOnUiThreadAfter(animator.Task, _ => this.Control.Invalidate());
                this.UpdateAnimationSpeed();
            }

            var animationTasks = this._animationTasks;
            if (animationTasks is not null) {
                this.Control.RunOnUiThreadAfter(
                    Task.WhenAll(animationTasks.Cast<Task>().Append(animator.Task))
                        .ContinueWith(
                            _ => {
                                if (mdlTask != this._mdlTask ||
                                    sklbTask != this._sklbTask ||
                                    animationTasks != this._animationTasks ||
                                    animator != this._animator)
                                    throw new OperationCanceledException();

                                animator.Result.ChangeAnimations(
                                    animationTasks
                                        .Where(x => x.IsCompletedSuccessfully)
                                        .Select(x => x.Result)
                                        .ToArray());
                                this.UpdateAnimationSpeed();
                            }),
                    _ => this.Control.Invalidate());
            } else {
                this.Control.RunOnUiThreadAfter(
                    animator.Task.ContinueWith(
                        _ => {
                            if (mdlTask != this._mdlTask || sklbTask != this._sklbTask ||
                                animationTasks != this._animationTasks ||
                                animator != this._animator)
                                throw new OperationCanceledException();

                            animator.Result.ChangeAnimations(null);
                            this.UpdateAnimationSpeed();
                        }),
                    _ => this.Control.Invalidate());
            }
        }
    }

    private void ParamCameraOnDataPull(ConstantBufferResource<CameraParameter> sender) =>
        this._paramCamera.UpdateData(
            CameraParameter.FromViewProjection(
                this.Control.Camera.View,
                this.Control.Camera.Projection));

    private void ParamWorldViewMatrixOnDataPull(ConstantBufferResource<WorldViewMatrix> sender) =>
        this._paramWorldViewMatrix.UpdateData(
            WorldViewMatrix.FromWorldView(Matrix4x4.Identity, this.Control.Camera.View));

    private void ParamWorldMiscOnDataPull(ConstantBufferResource<CustomMdlRendererShader.WorldMisc> sender) =>
        this._paramWorldMisc.UpdateData(
            CustomMdlRendererShader.WorldMisc.FromWorldViewProjection(
                Matrix4x4.Identity,
                this.Control.Camera.View,
                this.Control.Camera.Projection));

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget)
    {
        base.Draw3D(pRenderTarget);
        if (this._modelObject?.IsCompletedSuccessfully is true) {
            this._shader.BindCamera(this._paramCamera.Buffer);
            this._shader.BindWorldViewMatrix(this._paramWorldViewMatrix.Buffer);
            this._shader.BindMiscWorldCamera(this._paramWorldMisc.Buffer);
            this._shader.BindLight(this._paramLight.Buffer);

            if (this._animator is { IsCompletedSuccessfully: true }) {
                Span<nint> jointBuffers = stackalloc nint[this._animator.Result.BufferCount];
                this._animator.Result.UpdateAnimationStateAndGetBuffers(jointBuffers);
                this._shader.Draw(this._modelObject.Result, jointBuffers);
                this.AutoInvalidate = true;
            } else {
                this._shader.Draw(this._modelObject.Result, new());
                this.AutoInvalidate = false;
            }
        }
    }

    private void ResetCamera(MdlStructs.BoundingBoxStruct bboxTarget)
    {
        var occ = this.Control.ObjectCentricCamera;
        occ.Update(
            targetOffset: Vector3.Zero,
            targetBboxMin: new(bboxTarget.Min.AsSpan()),
            targetBboxMax: new(bboxTarget.Max.AsSpan()),
            yaw: 0,
            pitch: 0,
            resetDistance: true);
        this.ResetCamera();
    }

    private void ResetCamera()
    {
        this._paramCamera.EnablePull = this._paramWorldViewMatrix.EnablePull = this._paramWorldMisc.EnablePull = true;
        this.Control.Invalidate();
    }

    private void UpdateAnimationSpeed()
    {
        this._animator?.Task.ContinueWith(
            r => {
                if (r.IsCompletedSuccessfully)
                    r.Result.AnimationSpeed = this.Control.AnimationSpeed * (this.Control.AnimationPlaying ? 1 : 0);
                this.Control.Invalidate();
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }
}
