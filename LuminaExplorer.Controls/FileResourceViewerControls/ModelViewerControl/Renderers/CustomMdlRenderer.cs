using System;
using System.Numerics;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.DirectXStuff.Shaders;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public unsafe class CustomMdlRenderer : BaseMdlRenderer {
    private CustomMdlRendererShader _shader;
    private ConstantBufferResource<CameraParameter> _paramCamera;
    private ConstantBufferResource<WorldViewMatrix> _paramWorldViewMatrix;
    private ConstantBufferResource<JointMatrixArray> _paramJointMatrixArray;
    private ConstantBufferResource<CustomMdlRendererShader.WorldMisc> _paramWorldMisc;
    private ConstantBufferResource<CustomMdlRendererShader.LightParameters> _paramLight;
    private Task<MdlFile>? _modelTask;
    private Task<CustomMdlRendererShader.ModelObject>? _modelObject;

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
        _paramJointMatrixArray = new(Device, DeviceContext, true, JointMatrixArray.Default);
        _paramJointMatrixArray.DataPull += ParamJointMatrixArrayOnDataPull;
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
            _ = SafeDispose.OneAsync(ref _paramJointMatrixArray!);
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

                var bb = _modelTask.Result.ModelBoundingBoxes;
                var target = new Vector3(
                    (bb.Min[0] + bb.Max[0]) / 2f,
                    (bb.Min[1] + bb.Max[1]) / 2f,
                    (bb.Min[2] + bb.Max[2]) / 2f);
                var occ = Control.ObjectCentricCamera;
                occ.Update(
                    target: target,
                    yawFromTarget: MathF.PI,
                    pitchFromTarget: 0,
                    distance: 64 * (
                        target.X * occ.System.Forward.X +
                        target.Y + occ.System.Forward.Y +
                        target.Z + occ.System.Forward.Z));
                UpdateCamera();
            });
        });
    }

    private void ParamCameraOnDataPull(ConstantBufferResource<CameraParameter> sender) {
        _paramCamera.UpdateData(CameraParameter.FromViewProjection(
            Control.Camera.View,
            Control.Camera.Projection));
    }

    private void ParamWorldViewMatrixOnDataPull(ConstantBufferResource<WorldViewMatrix> sender) {
        _paramWorldViewMatrix.UpdateData(WorldViewMatrix.FromWorldView(Matrix4x4.Identity, Control.Camera.View));
    }

    private void ParamWorldMiscOnDataPull(ConstantBufferResource<CustomMdlRendererShader.WorldMisc> sender) {
        _paramWorldMisc.UpdateData(CustomMdlRendererShader.WorldMisc.FromWorldViewProjection(
            Matrix4x4.Identity,
            Control.Camera.View,
            Control.Camera.Projection));
    }

    private void ParamJointMatrixArrayOnDataPull(ConstantBufferResource<JointMatrixArray> sender) {
        // todo: fill this when animating
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        base.Draw3D(pRenderTarget);
        if (_modelObject?.IsCompletedSuccessfully is true) {
            _shader.BindCamera(_paramCamera.Buffer);
            _shader.BindWorldViewMatrix(_paramWorldViewMatrix.Buffer);
            _shader.BindJointMatrixArray(_paramJointMatrixArray.Buffer);
            _shader.BindMiscWorldCamera(_paramWorldMisc.Buffer);
            _shader.BindLight(_paramLight.Buffer);
            _shader.Draw(_modelObject.Result);
        }
    }

    private void UpdateCamera() {
        _paramCamera.EnablePull = _paramWorldViewMatrix.EnablePull = _paramWorldMisc.EnablePull = true;
        Control.Invalidate();
    }
}