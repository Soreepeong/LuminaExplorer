using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public unsafe class GamePixelShaderMdlRenderer : BaseMdlRenderer {
    private GameShaderPool _pool;
    private Task<MdlFile>? _modelTask;
    private ResultDisposingTask<ModelObjectWithGameShader>? _modelObject;
    private GameShaderState _shaderState;

    public GamePixelShaderMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null) { }

    public GamePixelShaderMdlRenderer(
        ModelViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext) {
        _pool = new(Device, DeviceContext);
        _pool.ShpkFileRequested += PoolOnShpkFileRequested;
        _shaderState = new(_pool);
        Control.ViewportChanged += ControlOnViewportChanged;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ClearModel();
            _ = SafeDispose.OneAsync(ref _shaderState!);
            _ = SafeDispose.OneAsync(ref _pool!);
        }

        base.Dispose(disposing);
    }

    public override void ClearModel() {
        _modelObject?.Task.ContinueWith(r => {
            if (r.IsCompletedSuccessfully) {
                var modelObject = r.Result;
                modelObject.DdsFileRequested -= ModelObjectOnDdsFileRequested;
                modelObject.MtrlFileRequested -= ModelObjectOnMtrlFileRequested;
                modelObject.ResourceLoadStateChanged -= ModelObjectOnLoadStateChanged;
            }
        });
        _ = SafeDispose.OneAsync(ref _modelObject);
        _ = SafeDispose.OneAsync(ref _shaderState!);
        _modelTask = null;
    }

    public override void SetModel(Task<MdlFile> mdlTask) {
        if (_modelTask == mdlTask)
            return;

        ClearModel();
        _modelTask = mdlTask;

        _modelObject = new(mdlTask.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully)
                throw r.Exception!;

            var modelObject = new ModelObjectWithGameShader(_pool, r.Result);
            modelObject.DdsFileRequested += ModelObjectOnDdsFileRequested;
            modelObject.MtrlFileRequested += ModelObjectOnMtrlFileRequested;
            modelObject.ResourceLoadStateChanged += ModelObjectOnLoadStateChanged;
            return modelObject;
        }, TaskScheduler.FromCurrentSynchronizationContext()));
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        base.Draw3D(pRenderTarget);
        // TODO
        if (_modelObject?.IsCompletedSuccessfully is true)
            _modelObject.Result.Draw(_shaderState);
    }

    private void PoolOnShpkFileRequested(string path, ref Task<ShpkFile?>? loader) =>
        loader ??= Control.GetTypedFileAsync<ShpkFile>(path);

    private void ControlOnViewportChanged() {
        // _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
    }
}