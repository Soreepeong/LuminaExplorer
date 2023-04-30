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
    private CustomMdlRendererShader _shader;
    private ConstantBufferResource<CameraParameter> _paramCamera;
    private ConstantBufferResource<WorldViewMatrix> _paramWorldViewMatrix;
    private ConstantBufferResource<CustomMdlRendererShader.WorldMisc> _paramWorldMisc;
    private ConstantBufferResource<CustomMdlRendererShader.LightParameters> _paramLight;
    private Task<MdlFile>? _modelTask;
    private Task<CustomMdlRendererShader.ModelObject>? _modelObject;

    private ResultDisposingTask<AnimatingJointsConstantBufferResource>? _animator;

    public CustomMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public CustomMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext) {
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
        _modelTask = null;
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

    public override void UpdateModel(Task<MdlFile> newModelTask) {
        if (_modelTask == newModelTask)
            return;

        ClearModel();
        _modelTask = newModelTask;

        newModelTask.ContinueWith(r => {
            if (_modelTask != newModelTask)
                return;

            _modelObject = Task.Run(() => {
                var modelObject = new CustomMdlRendererShader.ModelObject(_shader, r.Result);
                modelObject.DdsFileRequested += ModelObjectOnDdsFileRequested;
                modelObject.TextureLoadStateChanged += ModelObjectOnLoadStateChanged;
                modelObject.MtrlFileRequested += ModelObjectOnMtrlFileRequested;
                return modelObject;
            });
            Control.RunOnUiThreadAfter(_modelObject, _ => {
                if (_modelTask != newModelTask)
                    return;

                ResetCamera(_modelTask.Result.ModelBoundingBoxes);

                var sklbTask = Control.GetTypedFileAsync<SklbFile>(
                    "/chara/monster/m0361/skeleton/base/b0001/skl_m0361b0001.sklb");
                var papTask = Control.GetTypedFileAsync<PapFile>(
                    "/chara/monster/m0361/animation/a0001/bt_common/idle_sp/idle_sp_1.pap");
                    // "/chara/monster/m0361/animation/a0001/bt_common/resident/monster.pap");
                
                _animator = new(Task.WhenAll(sklbTask, papTask).ContinueWith(_ => {
                    if (!sklbTask.IsCompletedSuccessfully ||
                        !papTask.IsCompletedSuccessfully ||
                        sklbTask.Result is null ||
                        papTask.Result is null)
                        throw sklbTask.Exception ?? papTask.Exception ?? throw new();
                    var a = new AnimatingJointsConstantBufferResource(Device, DeviceContext, r.Result, sklbTask.Result);
                    a.Animation = AnimationSet.Decode(papTask.Result.GetAnimationBinding(0)); 
                    return a;
                }));
                Control.RunOnUiThreadAfter(_animator.Task, _ => Control.Invalidate());
            });
        });
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

            if (_animator?.IsCompletedSuccessfully is true) {
                Span<nint> jointBuffers = stackalloc nint[_animator.Result.BufferCount];
                _animator.Result.UpdateAnimationStateAndGetBuffers(jointBuffers);
                _shader.Draw(_modelObject.Result, jointBuffers);
                Control.BeginInvoke(Control.Invalidate);
            } else
                _shader.Draw(_modelObject.Result, new());
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
