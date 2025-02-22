using System;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public unsafe class GamePixelShaderMdlRenderer : BaseMdlRenderer {
    private GameShaderPool _pool;
    private Task<MdlFile>? _modelTask;
    private ResultDisposingTask<ModelObjectWithGameShader>? _modelObject;
    private GameShaderState _shaderState;

    public GamePixelShaderMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null)
    { }

    public GamePixelShaderMdlRenderer(
        ModelViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext)
    {
        this._pool = new(this.Device, this.DeviceContext);
        this._pool.ShpkFileRequested += this.PoolOnShpkFileRequested;
        this._shaderState = new(this._pool);
        this.Control.ViewportChanged += this.ControlOnViewportChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this.ModelTask = null;
            _ = SafeDispose.OneAsync(ref this._shaderState!);
            _ = SafeDispose.OneAsync(ref this._pool!);
        }

        base.Dispose(disposing);
    }

    public override Task<MdlFile>? ModelTask {
        get => this._modelTask;
        set {
            if (value == this._modelTask)
                return;

            void ClearModel()
            {
                this._modelObject?.Task.ContinueWith(
                    r => {
                        if (r.IsCompletedSuccessfully) {
                            var modelObject = r.Result;
                            modelObject.DdsFileRequested -= this.ModelObjectOnDdsFileRequested;
                            modelObject.MtrlFileRequested -= this.ModelObjectOnMtrlFileRequested;
                            modelObject.ResourceLoadStateChanged -= this.ModelObjectOnLoadStateChanged;
                        }
                    });
                _ = SafeDispose.OneAsync(ref this._modelObject);
                _ = SafeDispose.OneAsync(ref this._shaderState!);
                this._modelTask = null;
            }

            if (value is null) {
                ClearModel();
            } else {
                ClearModel();
                this._modelTask = value;

                this._modelObject = new(
                    value.ContinueWith(
                        r => {
                            if (!r.IsCompletedSuccessfully)
                                throw r.Exception!;

                            var modelObject = new ModelObjectWithGameShader(this._pool, r.Result);
                            modelObject.DdsFileRequested += this.ModelObjectOnDdsFileRequested;
                            modelObject.MtrlFileRequested += this.ModelObjectOnMtrlFileRequested;
                            modelObject.ResourceLoadStateChanged += this.ModelObjectOnLoadStateChanged;
                            return modelObject;
                        },
                        TaskScheduler.FromCurrentSynchronizationContext()));
            }
        }
    }

    public override Task<SklbFile[]>? SkeletonTask => null;

    public override Task<IAnimation>[]? AnimationsTask { get; set; }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget)
    {
        base.Draw3D(pRenderTarget);
        // TODO
        if (this._modelObject?.IsCompletedSuccessfully is true) this._modelObject.Result.Draw(this._shaderState);
    }

    private void PoolOnShpkFileRequested(string path, ref Task<ShpkFile?>? loader) =>
        loader ??= this.Control.GetTypedFileAsync<ShpkFile>(path);

    private void ControlOnViewportChanged(object? sender, EventArgs eventArgs)
    {
        // _shaderState.UpdateCamera(Matrix4x4.Identity, Control.Camera.View, Control.Camera.Projection);
    }
}
