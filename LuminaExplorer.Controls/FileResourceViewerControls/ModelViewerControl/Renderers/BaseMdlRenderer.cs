using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public abstract unsafe class BaseMdlRenderer : DirectXRenderer<ModelViewerControl> {
    protected BaseMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, true, pDevice, pDeviceContext)
    { }

    public abstract Task<MdlFile>? ModelTask { get; set; }

    public abstract Task<SklbFile[]>? SkeletonTask { get; }

    public abstract Task<IAnimation>[]? AnimationsTask { get; set; }

    protected void ModelObjectOnDdsFileRequested(string path, ref Task<DdsFile?>? loader)
    {
        loader ??= this.Control.GetTypedFileAsync<TexFile>(path)?.ContinueWith(
            r =>
                !r.IsCompletedSuccessfully ? null : r.Result?.ToDdsFileFollowGameDx11Conversion());
    }

    protected void ModelObjectOnMtrlFileRequested(string path, ref Task<MtrlFile?>? loader)
    {
        loader ??= this.Control.GetTypedFileAsync<MtrlFile>(path);
    }

    protected void ModelObjectOnLoadStateChanged()
    {
        this.Control.Invalidate();
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget)
    {
        var colors = stackalloc float[4];
        colors[0] = 1f * this.Control.BackColor.R / 255;
        colors[1] = 1f * this.Control.BackColor.G / 255;
        colors[2] = 1f * this.Control.BackColor.B / 255;
        colors[3] = 1f * this.Control.BackColor.A / 255;

        this.DeviceContext->ClearRenderTargetView(pRenderTarget, colors);
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget)
    {
        // empty
    }
}
