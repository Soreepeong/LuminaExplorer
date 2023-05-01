﻿using System;
using System.Threading.Tasks;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util.DdsStructs;
using Silk.NET.Direct2D;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

public abstract unsafe class BaseMdlRenderer : DirectXRenderer<ModelViewerControl> {
    protected BaseMdlRenderer(ModelViewerControl control, ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
        : base(control, true, pDevice, pDeviceContext) { }

    public abstract void ClearModel();

    public abstract void SetModel(Task<MdlFile> mdlTask);

    public virtual void SetAnimation(Task<IAnimation> animationTask) { }

    protected void ModelObjectOnDdsFileRequested(string path, ref Task<DdsFile?>? loader) {
        loader ??= Control.GetTypedFileAsync<TexFile>(path)?.ContinueWith(r =>
            !r.IsCompletedSuccessfully ? null : r.Result?.ToDdsFileFollowGameDx11Conversion());
    }

    protected void ModelObjectOnMtrlFileRequested(string path, ref Task<MtrlFile?>? loader) {
        loader ??= Control.GetTypedFileAsync<MtrlFile>(path);
    }

    protected void ModelObjectOnLoadStateChanged() {
        Control.Invalidate();
    }

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget) {
        Span<float> colors = stackalloc float[4];
        colors[0] = 1f * Control.BackColor.R / 255;
        colors[1] = 1f * Control.BackColor.G / 255;
        colors[2] = 1f * Control.BackColor.B / 255;
        colors[3] = 1f * Control.BackColor.A / 255;

        DeviceContext->ClearRenderTargetView(pRenderTarget, ref colors[0]);
    }

    protected override void Draw2D(ID2D1RenderTarget* pRenderTarget) {
        // empty
    }
}