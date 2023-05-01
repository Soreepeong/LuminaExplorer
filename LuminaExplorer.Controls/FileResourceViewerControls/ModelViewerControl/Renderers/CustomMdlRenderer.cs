using System;
using System.Numerics;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public unsafe class CustomMdlRenderer : BaseMdlRenderer {
    private readonly LruCache<string, SklbFile> _sklbCache = new(128, true);
    private readonly Task<ModelInfoResolver> _modelInfoResolver;

    private CustomMdlRendererShader _shader;
    private ConstantBufferResource<CameraParameter> _paramCamera;
    private ConstantBufferResource<WorldViewMatrix> _paramWorldViewMatrix;
    private ConstantBufferResource<CustomMdlRendererShader.WorldMisc> _paramWorldMisc;
    private ConstantBufferResource<CustomMdlRendererShader.LightParameters> _paramLight;
    private Task<MdlFile>? _mdlTask;
    private Task<SklbFile>? _sklbTask;
    private Task<PapFile>? _papTask;
    private Task<CustomMdlRendererShader.ModelObject>? _modelObject;

    private ResultDisposingTask<AnimatingJointsConstantBufferResource>? _animator;

    public CustomMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public CustomMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext) {
        _modelInfoResolver = ModelInfoResolver.GetResolver(control.GetTypedFileAsync<EstFile>);
        _shader = new(Device, DeviceContext);
        _paramCamera = new(Device, DeviceContext);
        _paramCamera.DataPull += ParamCameraOnDataPull;
        _paramWorldViewMatrix = new(Device, DeviceContext);
        _paramWorldViewMatrix.DataPull += ParamWorldViewMatrixOnDataPull;
        _paramWorldMisc = new(Device, DeviceContext);
        _paramWorldMisc.DataPull += ParamWorldMiscOnDataPull;
        _paramLight = new(Device, DeviceContext, false, CustomMdlRendererShader.LightParameters.Default);
        Control.ViewportChanged += UpdateCamera;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ClearModel();
            _ = SafeDispose.OneAsync(ref _shader!);
            _ = SafeDispose.OneAsync(ref _paramCamera!);
            _ = SafeDispose.OneAsync(ref _paramWorldViewMatrix!);
            _ = SafeDispose.OneAsync(ref _paramWorldMisc!);
            _ = SafeDispose.OneAsync(ref _paramLight!);
        }

        base.Dispose(disposing);
    }

    public override void ClearModel() {
        _mdlTask = null;
        _modelObject?.ContinueWith(r => {
            if (r.IsCompletedSuccessfully) {
                var modelObject = r.Result;
                modelObject.DdsFileRequested -= ModelObjectOnDdsFileRequested;
                modelObject.TextureLoadStateChanged -= ModelObjectOnLoadStateChanged;
                modelObject.MtrlFileRequested -= ModelObjectOnMtrlFileRequested;
            }
        });
        _modelObject = null;
        _ = SafeDispose.OneAsync(ref _animator);
    }

    public override void SetModel(Task<MdlFile> mdlTask) {
        if (_mdlTask == mdlTask)
            return;

        var prevTask = _mdlTask;
        _mdlTask = mdlTask;

        mdlTask.ContinueWith(r => {
            if (_mdlTask != mdlTask)
                return;
            if (prevTask?.IsCompletedSuccessfully is true && r.Result.FilePath == prevTask.Result.FilePath)
                return;

            ClearModel();
            _mdlTask = mdlTask;

            _modelObject = Task.Run(() => {
                var modelObject = new CustomMdlRendererShader.ModelObject(_shader, r.Result);
                modelObject.DdsFileRequested += ModelObjectOnDdsFileRequested;
                modelObject.TextureLoadStateChanged += ModelObjectOnLoadStateChanged;
                modelObject.MtrlFileRequested += ModelObjectOnMtrlFileRequested;
                return modelObject;
            });

            _sklbTask = Task.WhenAll(_modelObject, _modelInfoResolver).ContinueWith(_ => {
                if (_mdlTask != mdlTask)
                    throw new OperationCanceledException();
                if (!_modelInfoResolver.Result.TryFindSklbPath(mdlTask.Result.FilePath.Path, out var sklbPath))
                    throw new OperationCanceledException();
                if (_sklbCache.TryGet(sklbPath, out var sklb))
                    return Task.FromResult<SklbFile?>(sklb);
                return Control.GetTypedFileAsync<SklbFile>(sklbPath);
            }).Unwrap().ContinueWith(r2 => {
                if (r2.IsFaulted)
                    throw r2.Exception!;
                if (!r2.IsCompletedSuccessfully || r2.Result is null)
                    throw new("No associated skeleton file found");
                if (!_modelInfoResolver.Result.TryFindSklbPath(mdlTask.Result.FilePath.Path, out var sklbPath))
                    throw new FailFastException("?");
                _sklbCache.Add(sklbPath, r2.Result);

                LoadAnimationIfPossible();
                return r2.Result;
            });

            Control.RunOnUiThreadAfter(_modelObject, r2 => {
                if (_mdlTask != mdlTask || !r2.IsCompletedSuccessfully)
                    return;

                ResetCamera(_mdlTask.Result.ModelBoundingBoxes);
            });
        });
    }

    public override void SetAnimation(Task<PapFile> papTask) {
        if (_papTask == papTask)
            return;

        _papTask = papTask;
        LoadAnimationIfPossible();
    }

    private void LoadAnimationIfPossible() {
        var mdlTask = _mdlTask;
        var sklbTask = _sklbTask;
        if (mdlTask is not null && sklbTask is not null) {
            var animator = _animator;
            if (animator is null) {
                _animator = animator = new(Task.Run(() => new AnimatingJointsConstantBufferResource(
                    Device, DeviceContext, mdlTask.Result, sklbTask.Result)));
                Control.RunOnUiThreadAfter(animator.Task, _ => Control.Invalidate());
            }

            var papTask = _papTask;
            if (papTask is not null) {
                Control.RunOnUiThreadAfter(Task.WhenAll(animator.Task, papTask).ContinueWith(_ => {
                    if (mdlTask != _mdlTask || sklbTask != _sklbTask || papTask != _papTask || animator != _animator)
                        throw new OperationCanceledException();

                    animator.Result.ChangeAnimation(AnimationSet.Decode(papTask.Result.GetAnimationBindingNode(0)));
                }), _ => Control.Invalidate());
            }
        }
    }

    private void ParamCameraOnDataPull(ConstantBufferResource<CameraParameter> sender) =>
        _paramCamera.UpdateData(CameraParameter.FromViewProjection(
            Control.Camera.View,
            Control.Camera.Projection));

    private void ParamWorldViewMatrixOnDataPull(ConstantBufferResource<WorldViewMatrix> sender) =>
        _paramWorldViewMatrix.UpdateData(WorldViewMatrix.FromWorldView(Matrix4x4.Identity, Control.Camera.View));

    private void ParamWorldMiscOnDataPull(ConstantBufferResource<CustomMdlRendererShader.WorldMisc> sender) =>
        _paramWorldMisc.UpdateData(CustomMdlRendererShader.WorldMisc.FromWorldViewProjection(
            Matrix4x4.Identity,
            Control.Camera.View,
            Control.Camera.Projection));

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        base.Draw3D(pRenderTarget);
        if (_modelObject?.IsCompletedSuccessfully is true) {
            _shader.BindCamera(_paramCamera.Buffer);
            _shader.BindWorldViewMatrix(_paramWorldViewMatrix.Buffer);
            _shader.BindMiscWorldCamera(_paramWorldMisc.Buffer);
            _shader.BindLight(_paramLight.Buffer);

            if (_animator is {IsCompletedSuccessfully: true, Result.HasActiveAnimation: true}) {
                Span<nint> jointBuffers = stackalloc nint[_animator.Result.BufferCount];
                _animator.Result.UpdateAnimationStateAndGetBuffers(jointBuffers);
                _shader.Draw(_modelObject.Result, jointBuffers);
                AutoInvalidate = true;
            } else {
                _shader.Draw(_modelObject.Result, new());
                AutoInvalidate = false;
            }
        }
    }

    private void ResetCamera(MdlStructs.BoundingBoxStruct bboxTarget) {
        var occ = Control.ObjectCentricCamera;
        occ.Update(
            targetOffset: Vector3.Zero,
            targetBboxMin: new(bboxTarget.Min.AsSpan()),
            targetBboxMax: new(bboxTarget.Max.AsSpan()),
            yaw: 0,
            pitch: 0,
            resetDistance: true);
        UpdateCamera();
    }

    private void UpdateCamera() {
        _paramCamera.EnablePull = _paramWorldViewMatrix.EnablePull = _paramWorldMisc.EnablePull = true;
        Control.Invalidate();
    }
}
