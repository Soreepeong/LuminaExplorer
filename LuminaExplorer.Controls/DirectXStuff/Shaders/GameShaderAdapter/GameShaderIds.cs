using Lumina.Misc;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// CRC32 identifiers of names used in shader packages, such as pass names, shader key values, and resource names.
/// </summary>
public static class GameShaderIds {
    public static readonly uint PassZOpaque = Crc("PASS_Z_OPAQUE");
    public static readonly uint PassGOpaque = Crc("PASS_G_OPAQUE");
    public static readonly uint PassGSemiTransparency = Crc("PASS_G_SEMITRANSPARENCY");
    public static readonly uint PassCompositeOpaque = Crc("PASS_COMPOSITE_OPAQUE");
    public static readonly uint PassCompositeSemiTransparency = Crc("PASS_COMPOSITE_SEMITRANSPARENCY");
    public static readonly uint PassSemiTransparency = Crc("PASS_SEMITRANSPARENCY");
    public static readonly uint PassLightingOpaque = Crc("PASS_LIGHTING_OPAQUE");
    public static readonly uint Pass0 = Crc("PASS_0");

    public static readonly uint SubViewMain = Crc("SUB_VIEW_MAIN");

    // Scene keys
    public static readonly uint ApplyAlphaClip = Crc("ApplyAlphaClip");

    /// <summary>Value of <see cref="ApplyAlphaClip"/> that selects shaders that discard pixels using
    /// g_AlphaThreshold. The name is unknown; the default value is 0x7D5081DF.</summary>
    public const uint ApplyAlphaClipOn = 0x59C4E6DB;

    /// <summary>Selects how vertices are transformed into the view space.</summary>
    public static readonly uint TransformView = Crc("TransformView");

    /// <summary>Value of <see cref="TransformView"/> that selects shaders transforming vertices using
    /// g_WorldViewMatrix. This is the default value.</summary>
    public static readonly uint TransformViewRigid = Crc("TransformViewRigid");

    /// <summary>Value of <see cref="TransformView"/> that selects shaders skinning vertices using
    /// g_JointMatrixArray, which holds joint matrices that transform from the model space into the view space.
    /// </summary>
    public static readonly uint TransformViewSkin = Crc("TransformViewSkin");

    // Material parameters
    public static readonly uint AlphaThreshold = Crc("g_AlphaThreshold");

    // Constant buffers
    public static readonly uint CameraParameter = Crc("g_CameraParameter");
    public static readonly uint WorldViewMatrix = Crc("g_WorldViewMatrix");
    public static readonly uint InstanceParameter = Crc("g_InstanceParameter");
    public static readonly uint ModelParameter = Crc("g_ModelParameter");
    public static readonly uint MaterialParameter = Crc("g_MaterialParameter");
    public static readonly uint MaterialParameterDynamic = Crc("g_MaterialParameterDynamic");
    public static readonly uint CommonParameter = Crc("g_CommonParameter");
    public static readonly uint PbrParameterCommon = Crc("g_PbrParameterCommon");
    public static readonly uint AmbientParam = Crc("g_AmbientParam");
    public static readonly uint CustomizeParameter = Crc("g_CustomizeParameter");
    public static readonly uint DecalColor = Crc("g_DecalColor");
    public static readonly uint FogParameter = Crc("g_FogParameter");
    public static readonly uint SceneParameter = Crc("g_SceneParameter");

    // Structured buffers
    public static readonly uint JointMatrixArray = Crc("g_JointMatrixArray");
    public static readonly uint JointMatrixArrayPrev = Crc("g_JointMatrixArrayPrev");

    // Textures and samplers
    public static readonly uint SamplerGBuffer = Crc("g_SamplerGBuffer");
    public static readonly uint SamplerGBuffer1 = Crc("g_SamplerGBuffer1");
    public static readonly uint SamplerGBuffer2 = Crc("g_SamplerGBuffer2");
    public static readonly uint SamplerGBuffer3 = Crc("g_SamplerGBuffer3");
    public static readonly uint SamplerLightDiffuse = Crc("g_SamplerLightDiffuse");
    public static readonly uint SamplerLightSpecular = Crc("g_SamplerLightSpecular");
    public static readonly uint SamplerOcclusion = Crc("g_SamplerOcclusion");
    public static readonly uint SamplerReflectionArray = Crc("g_SamplerReflectionArray");
    public static readonly uint SamplerTable = Crc("g_SamplerTable");
    public static readonly uint SamplerTileOrb = Crc("g_SamplerTileOrb");
    public static readonly uint SamplerTileNormal = Crc("g_SamplerTileNormal");
    public static readonly uint SamplerSphereMap = Crc("g_SamplerSphereMap");
    public static readonly uint SamplerIndex = Crc("g_SamplerIndex");
    public static readonly uint SamplerNormal = Crc("g_SamplerNormal");
    public static readonly uint SamplerNormal2 = Crc("g_SamplerNormal2");
    public static readonly uint SamplerDecal = Crc("g_SamplerDecal");
    public static readonly uint SamplerDither = Crc("g_SamplerDither");
    public static readonly uint SamplerDiffuse = Crc("g_SamplerDiffuse");
    public static readonly uint SamplerMask = Crc("g_SamplerMask");
    public static readonly uint SamplerWrinklesMask = Crc("g_SamplerWrinklesMask");
    public static readonly uint FogWeightLutSampler = Crc("g_FogWeightLutSampler");
    public static readonly uint SkySampler = Crc("g_SkySampler");

    public static uint Crc(string name) => Crc32.Get(name, 0xFFFFFFFFu);
}
