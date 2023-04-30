namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles; 

public enum InputId : uint {
    // From characters.shpk
    CameraParameter = 0xF0BAD919u,
    WorldViewMatrix = 0x76BB3DC0u,
    InstanceParameter = 0x20A30B34u,
    ModelParameter = 0x4E0A5472u,
    JointMatrixArray = 0x88AA546Au,
    MaterialParameter = 0x64D12851u,
    SamplerNormal = 0x0C5EC1F1u,
    SceneParameter = 0x3D086484u,
    SamplerTable = 0x2005679Fu,
    SamplerTileNormal = 0x92F03E53u,
    SamplerIndex = 0x565F8FD8u,
    CommonParameter = 0xA9442826u,
    MaterialParameterDynamic = 0x77F6BFB3u,
    AmbientParam = 0xA296769Fu,
    SamplerLightDiffuse = 0x23D0F850u,
    SamplerLightSpecular = 0x6C19ACA4u,
    SamplerGBuffer = 0xEBBB29BDu,
    SamplerMask = 0x8A4E82B6u,
    SamplerTileDiffuse = 0x29156A85u,
    SamplerReflection = 0x87F6474Du,
    SamplerOcclusion = 0x32667BD7u,
    SamplerDither = 0x9F467267u,
    SamplerDiffuse = 0x115306BEu,
    SamplerSpecular = 0x2B99E025u,
    DecalColor = 0x5B0F708Cu,
    SamplerDecal = 0x0237CB94u,
    LightDirection = 0xEF4E7491u,
    
    // TODO: dig into shpk files
    BackgroundInstanceData = 0xEC4CCAA5u,
    InstancingData = 0xC7DB2357u,
}
