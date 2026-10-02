using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl.Renderers;

/// <summary>
/// Renders models using the shaders shipped with the game.
/// </summary>
/// <remarks>
/// <para>The game renders characters using deferred shading: G passes write normals and material properties into
/// G-buffers, lighting passes accumulate lights into light buffers, and composite passes draw the models again to
/// combine the light buffers with the material colors. This renderer runs the game's G and composite passes as-is,
/// and replaces the lighting passes with a simple one (a key light and a fill light relative to the camera), as the
/// game's lighting passes depend on data the model viewer does not have, such as shadow maps and light lists.
/// Ambient light is supplied through g_AmbientParam.</para>
/// <para>Transparent materials (such as hair) are drawn twice, as the game does: once with the opaque passes, which
/// discard pixels below the alpha threshold, and then with PASS_G_SEMITRANSPARENCY, lighting, and
/// PASS_COMPOSITE_SEMITRANSPARENCY alpha blended over the result. Shader packages without deferred passes are drawn
/// last with a single alpha blended pass, and those that only blend into the G-buffers (decals and occlusion) are not
/// drawn.</para>
/// <para>Shader nodes are selected with the default system and scene keys, the material keys from the material,
/// and sub view keys for the main view. Skinned meshes use the nodes with the TransformView scene key set to
/// TransformViewSkin, whose vertex shaders transform vertices using joint matrices (from the model space directly into
/// the view space) read from g_JointMatrixArray, indexed through the bone table of each mesh. The joint matrices are
/// computed from the skeletons and the selected animations, as done for <see cref="CustomMdlRenderer"/>, and are
/// updated every frame.</para>
/// </remarks>
public unsafe class GameShaderMdlRenderer : BaseMdlRenderer {
    /// <summary>Identifier of the constant buffer used by the lighting pass; not used by game shaders.</summary>
    private const uint LightParameterId = 0x4C494748;

    private readonly LruCache<string, SklbFile> _sklbCache = new(128, true);

    private GameShaderPool _pool;
    private GameShaderState _state;
    private Task<MdlFile>? _mdlTask;
    private ResultDisposingTask<ModelObjectWithGameShader>? _modelObject;
    private Task<SklbFile[]>? _sklbTask;
    private Task<IAnimation>[]? _animationTasks;
    private ResultDisposingTask<AnimatingJointsConstantBufferResource>? _animator;

    public GameShaderMdlRenderer(ModelViewerControl control)
        // ReSharper disable once IntroduceOptionalParameters.Global
        : this(control, null, null)
    { }

    public GameShaderMdlRenderer(
        ModelViewerControl control,
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext)
        : base(control, pDevice, pDeviceContext)
    {
        this._pool = new(this.Device, this.DeviceContext);
        this._pool.ShpkFileRequested += this.PoolOnShpkFileRequested;
        this._pool.TexFileRequested += this.PoolOnTexFileRequested;
        this._pool.ResourceLoadStateChanged += this.ModelObjectOnLoadStateChanged;
        this._state = new(this._pool);
        this.Control.ViewportChanged += this.ControlOnViewportChanged;
        this.Control.AnimationSpeedChanged += this.ControlOnAnimationStateChanged;
        this.Control.AnimationPlayingChanged += this.ControlOnAnimationStateChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this.Control.ViewportChanged -= this.ControlOnViewportChanged;
            this.Control.AnimationSpeedChanged -= this.ControlOnAnimationStateChanged;
            this.Control.AnimationPlayingChanged -= this.ControlOnAnimationStateChanged;
            this.ModelTask = null;
            _ = SafeDispose.OneAsync(ref this._state!);
            _ = SafeDispose.OneAsync(ref this._pool!);
        }

        base.Dispose(disposing);
    }

    public override Task<MdlFile>? ModelTask {
        get => this._mdlTask;
        set {
            if (value == this._mdlTask)
                return;

            this._modelObject?.Task.ContinueWith(
                r => {
                    if (!r.IsCompletedSuccessfully)
                        return;
                    r.Result.DdsFileRequested -= this.ModelObjectOnDdsFileRequested;
                    r.Result.MtrlFileRequested -= this.ModelObjectOnMtrlFileRequested;
                    r.Result.ResourceLoadStateChanged -= this.ModelObjectOnLoadStateChanged;
                });
            _ = SafeDispose.OneAsync(ref this._modelObject);
            _ = SafeDispose.OneAsync(ref this._animator);
            this._sklbTask = null;
            this._mdlTask = value;
            if (value is null)
                return;

            var pool = this._pool;
            this._modelObject = new(
                value.ContinueWith(
                    r => {
                        var modelObject = new ModelObjectWithGameShader(pool, r.Result);
                        modelObject.DdsFileRequested += this.ModelObjectOnDdsFileRequested;
                        modelObject.MtrlFileRequested += this.ModelObjectOnMtrlFileRequested;
                        modelObject.ResourceLoadStateChanged += this.ModelObjectOnLoadStateChanged;
                        return modelObject;
                    },
                    TaskContinuationOptions.OnlyOnRanToCompletion));

            this.Control.RunOnUiThreadAfter(
                this._modelObject.Task,
                r => {
                    if (this._mdlTask != value || !r.IsCompletedSuccessfully)
                        return;

                    this.ResetCamera(value.Result.ModelBoundingBoxes);
                });

            var resolverTask = this.Control.ModelInfoResolverTask ??= ModelInfoResolver.GetResolver(
                this.Control.GetTypedFileAsync<EstFile>,
                this.Control.GetTypedFileAsync<PbdFile>);
            var sklbTask = this._sklbTask = Task
                .WhenAll(value, resolverTask)
                .ContinueWith(
                    _ => {
                        if (this._mdlTask != value)
                            throw new OperationCanceledException();
                        return Task.WhenAll(
                            resolverTask.Result
                                .FindSklbPath(value.Result.FilePath.Path)
                                .Select(
                                    x => this._sklbCache.TryGet(x, out var sklb)
                                        ? Task.FromResult(Tuple.Create(x, (SklbFile?) sklb))
                                        : this.Control.GetTypedFileAsync<SklbFile>(x)
                                            .ContinueWith(r2 => Tuple.Create(x, r2.Result))));
                    })
                .Unwrap()
                .ContinueWith(
                    r => {
                        if (r.IsFaulted)
                            throw r.Exception!;
                        if (!r.IsCompletedSuccessfully)
                            throw new OperationCanceledException();
                        foreach (var (sklbPath, sklb) in r.Result) {
                            if (sklb is not null)
                                this._sklbCache.Add(sklbPath, sklb);
                        }

                        var sklbs = r.Result.Select(x => x.Item2).Where(x => x is not null).Select(x => x!).ToArray();
                        if (sklbs.Length == 0)
                            throw new("No associated skeleton file found");
                        return sklbs;
                    });

            this.Control.RunOnUiThreadAfter(
                sklbTask,
                _ => {
                    if (this._sklbTask == sklbTask)
                        this.LoadAnimationIfPossible();
                });
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

    /// <summary>Creates the animator once the skeletons are loaded, and applies the selected animations to it.
    /// </summary>
    /// <remarks>Must be called from the UI thread.</remarks>
    private void LoadAnimationIfPossible()
    {
        var mdlTask = this._mdlTask;
        var sklbTask = this._sklbTask;
        if (mdlTask is null || sklbTask is not { IsCompletedSuccessfully: true })
            return;

        var animator = this._animator;
        if (animator is null) {
            var pDevice = this.Device;
            var pDeviceContext = this.DeviceContext;
            this._animator = animator = new(
                Task.Run(
                    () => new AnimatingJointsConstantBufferResource(
                        pDevice,
                        pDeviceContext,
                        mdlTask.Result,
                        sklbTask.Result)));
        }

        var animationTasks = this._animationTasks;
        this.Control.RunOnUiThreadAfter(
            Task.WhenAll((animationTasks ?? []).Cast<Task>().Append(animator.Task)),
            _ => {
                if (mdlTask != this._mdlTask ||
                    sklbTask != this._sklbTask ||
                    animationTasks != this._animationTasks ||
                    animator != this._animator ||
                    !animator.IsCompletedSuccessfully)
                    return;

                animator.Result.ChangeAnimations(
                    animationTasks?
                        .Where(x => x.IsCompletedSuccessfully)
                        .Select(x => x.Result)
                        .ToArray());
                this.UpdateAnimationSpeed();
            });
    }

    private void UpdateAnimationSpeed()
    {
        if (this._animator is { IsCompletedSuccessfully: true } animator)
            animator.Result.AnimationSpeed = this.Control.AnimationSpeed * (this.Control.AnimationPlaying ? 1 : 0);
        this.Control.Invalidate();
    }

    private void ControlOnAnimationStateChanged(object? sender, EventArgs e) => this.UpdateAnimationSpeed();

    /// <summary>Gets the materials of the current model that have been prepared so far, for diagnostics.</summary>
    public string DescribeMaterials() =>
        this._modelObject?.IsCompletedSuccessfully is true
            ? string.Join("\n", this._modelObject.Result.LoadedMaterials) + "\n" +
            this._pool.DescribeGlobalTextures()
            : string.Empty;

    protected override void Draw3D(ID3D11RenderTargetView* pRenderTarget)
    {
        base.Draw3D(pRenderTarget);
        if (this._modelObject?.IsCompletedSuccessfully is not true)
            return;

        var model = this._modelObject.Result;
        var ctx = this.DeviceContext;
        var width = Math.Max(1, this.Control.Width);
        var height = Math.Max(1, this.Control.Height);
        this._state.EnsureRenderTargets(width, height);
        this.UpdateConstants(width, height);

        // Joint matrices of skinned meshes transform from the model space into the view space.
        var modelView = this.Control.Camera.View;
        if (this._animator is { IsCompletedSuccessfully: true } animator) {
            animator.Result.UpdateAnimationState();
            model.UpdateJointMatrices(modelView, animator.Result);
            this.AutoInvalidate = true;
        } else {
            model.UpdateJointMatrices(modelView, null);
            this.AutoInvalidate = false;
        }

        var pDepthStencilView = this.DepthStencilView;

        // Opaque materials: G pass, lighting, and then composite pass.
        this.DrawGBuffers(model, ModelObjectWithGameShader.DrawStage.GBuffer, pDepthStencilView);
        this.DrawLighting();
        this._state.UnbindShaderResources();
        ctx->OMSetRenderTargets(1, &pRenderTarget, pDepthStencilView);
        ctx->OMSetDepthStencilState(this._pool.DepthTestOnly, 0);
        ctx->OMSetBlendState(this._pool.BlendOpaque, null, uint.MaxValue);
        model.Draw(this._state, ModelObjectWithGameShader.DrawStage.Composite);

        // Semi-transparent materials: the same, but alpha blended over the result so far.
        if (model.HasSemiTransparentMaterials) {
            this.DrawGBuffers(
                model,
                ModelObjectWithGameShader.DrawStage.GBufferSemiTransparent,
                pDepthStencilView);
            this.DrawLighting();
            this._state.UnbindShaderResources();
            ctx->OMSetRenderTargets(1, &pRenderTarget, pDepthStencilView);
            ctx->OMSetDepthStencilState(this._pool.DepthTestOnly, 0);
            ctx->OMSetBlendState(this._pool.BlendAlpha, null, uint.MaxValue);
            model.Draw(this._state, ModelObjectWithGameShader.DrawStage.CompositeSemiTransparent);
        }

        // Materials without deferred passes.
        ctx->OMSetBlendState(this._pool.BlendAlpha, null, uint.MaxValue);
        model.Draw(this._state, ModelObjectWithGameShader.DrawStage.Forward);

        this._state.UnbindShaderResources();
        ctx->OMSetBlendState(null, null, uint.MaxValue);
        ctx->OMSetDepthStencilState(null, 0);
        ctx->RSSetState(null);
    }

    private void DrawGBuffers(
        ModelObjectWithGameShader model,
        ModelObjectWithGameShader.DrawStage stage,
        ID3D11DepthStencilView* pDepthStencilView)
    {
        var ctx = this.DeviceContext;
        var zero = stackalloc float[4] { 0, 0, 0, 0 };
        this._state.UnbindShaderResources();
        var rtvs = stackalloc ID3D11RenderTargetView*[GameShaderState.GBufferCount];
        for (var i = 0; i < GameShaderState.GBufferCount; i++) {
            rtvs[i] = this._state.GetGBufferRenderTarget(i);
            ctx->ClearRenderTargetView(rtvs[i], zero);
        }

        ctx->OMSetRenderTargets(GameShaderState.GBufferCount, rtvs, pDepthStencilView);
        ctx->OMSetDepthStencilState(this._pool.DepthWrite, 0);
        ctx->OMSetBlendState(this._pool.BlendOpaque, null, uint.MaxValue);
        model.Draw(this._state, stage);
    }

    /// <summary>Fills the light buffers from the G-buffers, in place of the game's lighting passes.</summary>
    private void DrawLighting()
    {
        var ctx = this.DeviceContext;
        var zero = stackalloc float[4] { 0, 0, 0, 0 };
        this._state.UnbindShaderResources();
        var rtvs = stackalloc ID3D11RenderTargetView*[GameShaderState.LightBufferCount];
        for (var i = 0; i < GameShaderState.LightBufferCount; i++) {
            rtvs[i] = this._state.GetLightBufferRenderTarget(i);
            ctx->ClearRenderTargetView(rtvs[i], zero);
        }

        ctx->OMSetRenderTargets(GameShaderState.LightBufferCount, rtvs, null);
        ctx->OMSetDepthStencilState(this._pool.DepthDisabled, 0);
        ctx->OMSetBlendState(this._pool.BlendOpaque, null, uint.MaxValue);
        ctx->RSSetState(this._pool.RasterizerCullNone);
        ctx->IASetInputLayout(null);
        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->VSSetShader(this._pool.LightingVertexShader, null, 0);
        ctx->PSSetShader(this._pool.LightingPixelShader, null, 0);
        var srvs = stackalloc ID3D11ShaderResourceView*[2] {
            this._state.GetGBufferTexture(0),
            this._state.GetGBufferTexture(1),
        };
        ctx->PSSetShaderResources(0, 2, srvs);
        var pLightBuffer = this._state.GetBuffer(LightParameterId, 0);
        ctx->PSSetConstantBuffers(0, 1, &pLightBuffer);
        ctx->Draw(3, 0);
    }

    private void UpdateConstants(int width, int height)
    {
        var view = this.Control.Camera.View;
        var projection = this.Control.Camera.Projection;
        var world = Matrix4x4.Identity;
        var viewProjection = view * projection;
        var inverseView = Matrix4x4.Invert(view, out var t) ? t : Matrix4x4.Identity;
        var inverseProjection = Matrix4x4.Invert(projection, out t) ? t : Matrix4x4.Identity;
        var inverseViewProjection = Matrix4x4.Invert(viewProjection, out t) ? t : Matrix4x4.Identity;

        // Game shaders use row_major matrices multiplied as mul(M, v), so matrices are stored transposed.
        var s = this._state;
        var id = GameShaderIds.CameraParameter;
        s.GetDataForWriting(id, 944).Clear();
        WriteMatrix3X4(s, id, 0x000, view);
        WriteMatrix3X4(s, id, 0x030, inverseView);
        WriteMatrix4X4(s, id, 0x060, viewProjection);
        WriteMatrix4X4(s, id, 0x0A0, inverseViewProjection);
        WriteMatrix4X4(s, id, 0x0E0, inverseProjection);
        WriteMatrix4X4(s, id, 0x120, projection);
        WriteMatrix4X4(s, id, 0x160, projection); // m_MainViewToProjectionMatrix
        WriteMatrix3X4(s, id, 0x1A0, view); // m_ViewMatrixPrev
        WriteMatrix3X4(s, id, 0x1D0, inverseView);
        WriteMatrix4X4(s, id, 0x200, viewProjection);
        WriteMatrix4X4(s, id, 0x240, inverseViewProjection);
        WriteMatrix4X4(s, id, 0x280, projection);
        WriteMatrix4X4(s, id, 0x2C0, inverseProjection);
        WriteMatrix4X4(s, id, 0x300, Matrix4x4.Identity); // m_ProjToProjPrevMatrix
        WriteMatrix3X4(s, id, 0x340, Matrix4x4.Identity); // m_ViewToViewPrevMatrix
        WriteMatrix3X4(s, id, 0x370, inverseView); // m_MainViewToWorldMatrix

        // g_WorldViewMatrix[2]: current and previous frame.
        id = GameShaderIds.WorldViewMatrix;
        WriteMatrix3X4(s, id, 0x00, world * view);
        WriteMatrix3X4(s, id, 0x30, world * view);

        id = GameShaderIds.InstanceParameter;
        s.GetDataForWriting(id, 176).Clear();
        s.Write(id, 0x00, Vector4.One); // m_MulColor
        s.Write(id, 0x10, Vector4.One); // m_EnvParameter
        s.Write(id, 0x20, new Vector4(0.2f, 0.2f, 0, 0)); // m_CameraLight.m_DiffuseSpecular
        s.Write(id, 0xA0, new Vector4(0, 1, 0, 0)); // m_HeadUpVector

        s.GetDataForWriting(GameShaderIds.ModelParameter, 16).Clear();
        s.GetDataForWriting(GameShaderIds.PbrParameterCommon, 80).Clear();
        s.GetDataForWriting(GameShaderIds.MaterialParameterDynamic, 16).Clear();

        id = GameShaderIds.CommonParameter;
        s.Write(id, 0x00, new Vector4(1f / width, 1f / height, 0, 0)); // m_RenderTarget
        s.Write(id, 0x10, new Vector4(0, 0, width, height)); // m_Viewport
        s.Write(id, 0x20, new Vector4(1, 0, 0, 1)); // m_Misc
        s.Write(id, 0x30, new Vector4(1, 0, 0, 0)); // m_Misc2

        // Ambient light, as spherical harmonics (per color channel; evaluated as dot(float4(n, 1), sh) and then
        // squared) in view space. Slightly brighter from above.
        id = GameShaderIds.AmbientParam;
        var sh = new Vector4(0, 0.12f, 0, 0.5f);
        s.Write(id, 0x00, sh);
        s.Write(id, 0x10, sh);
        s.Write(id, 0x20, sh);
        s.Write(id, 0x30, new Vector4(1, 1, 1, 1)); // color, shRecipScale
        s.Write(id, 0x40, new Vector4(0, 1, 1, 1)); // attenuation slope/intercept/value, skyVisibility
        s.Write(id, 0x50, new Vector4(0, 0, 0, 0)); // reflection scale/offset, bakeLightRate, envLocationIndex
        s.Write(id, 0x60, sh);
        s.Write(id, 0x70, sh);
        s.Write(id, 0x80, sh);
        s.Write(id, 0x90, new Vector4(1, 0, 0, 1)); // skyShRecipScale, envLocationIndexPrev/InterpRate, invAmbientScale

        id = GameShaderIds.CustomizeParameter;
        s.Write(id, 0x00, new Vector4(0.87f, 0.68f, 0.57f, 1)); // m_SkinColor
        s.Write(id, 0x10, new Vector4(0.62f, 0.36f, 0.36f, 1)); // m_LipColor
        s.Write(id, 0x20, new Vector4(0.45f, 0.33f, 0.24f, 1)); // m_MainColor (hair)
        s.Write(id, 0x30, new Vector4(0.6f, 0.45f, 0.3f, 1)); // m_MeshColor (hair highlight)
        s.Write(id, 0x40, new Vector4(0.3f, 0.45f, 0.6f, 1)); // m_LeftColor (eye)
        s.Write(id, 0x50, new Vector4(0.3f, 0.45f, 0.6f, 1)); // m_RightColor (eye)
        s.Write(id, 0x60, new Vector4(1, 1, 1, 0)); // m_OptionColor0

        s.GetDataForWriting(GameShaderIds.DecalColor, 16).Clear();
        s.GetDataForWriting(GameShaderIds.FogParameter, 160).Clear();

        // Lighting pass parameters; see GameShaderPool.LightingShaderSource.
        id = LightParameterId;
        s.Write(id, 0x00, view);
        s.Write(id, 0x40, Vector4.Normalize(new(-0.5f, 0.6f, 0.65f, 0)));
        s.Write(id, 0x50, new Vector4(1f, 0.97f, 0.92f, 1));
        s.Write(id, 0x60, Vector4.Normalize(new(0.6f, -0.1f, 0.5f, 0)));
        s.Write(id, 0x70, new Vector4(0.25f, 0.27f, 0.3f, 1));
        s.Write(id, 0x80, new Vector4(0.4f, 24f, 0, 0));
    }

    private static void WriteMatrix4X4(GameShaderState state, uint id, int offset, Matrix4x4 m) =>
        state.Write(id, offset, Matrix4x4.Transpose(m));

    private static void WriteMatrix3X4(GameShaderState state, uint id, int offset, Matrix4x4 m)
    {
        var transposed = Matrix4x4.Transpose(m);
        state.Write(id, offset, new ReadOnlySpan<float>(&transposed, 12));
    }

    private void PoolOnShpkFileRequested(string path, ref Task<ShpkFile?>? loader) =>
        loader ??= this.Control.GetTypedFileAsync<ShpkFile>(path);

    private void PoolOnTexFileRequested(string path, ref Task<TexFile?>? loader) =>
        loader ??= this.Control.GetTypedFileAsync<TexFile>(path);

    private void ControlOnViewportChanged(object? sender, EventArgs eventArgs) => this.Control.Invalidate();

    private void ResetCamera(MdlStructs.BoundingBoxStruct bboxTarget)
    {
        this.Control.ObjectCentricCamera.Update(
            targetOffset: Vector3.Zero,
            targetBboxMin: new(bboxTarget.Min.AsSpan()),
            targetBboxMax: new(bboxTarget.Max.AsSpan()),
            yaw: 0,
            pitch: 0,
            resetDistance: true);
        this.Control.Invalidate();
    }
}
