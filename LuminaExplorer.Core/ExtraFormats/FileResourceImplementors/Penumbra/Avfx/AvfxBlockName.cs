using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Avfx;

/// <summary>Names of AVFX blocks. A name is stored as a little endian uint, so the string reads from the most
/// significant byte; leading zero bytes are omitted.</summary>
public static class AvfxBlockName {
    public const uint Avfx = 0x41564658; // AVFX
    public const uint Version = 0x00566572; // Ver
    public const uint IsDelayFastParticle = 0x62444650; // bDFP
    public const uint IsFitGround = 0x00624647; // bFG
    public const uint IsTransformSkip = 0x00625453; // bTS
    public const uint IsAllStopOnHide = 0x62415348; // bASH
    public const uint CanBeClippedOut = 0x62434243; // bCBC
    public const uint ClipBoxEnabled = 0x6243756C; // bCul
    public const uint ClipBoxX = 0x43425078; // CBPx
    public const uint ClipBoxY = 0x43425079; // CBPy
    public const uint ClipBoxZ = 0x4342507A; // CBPz
    public const uint ClipBoxSizeX = 0x43425378; // CBSx
    public const uint ClipBoxSizeY = 0x43425379; // CBSy
    public const uint ClipBoxSizeZ = 0x4342537A; // CBSz
    public const uint BiasZmaxScale = 0x5A424D73; // ZBMs
    public const uint BiasZmaxDistance = 0x5A424D64; // ZBMd
    public const uint IsCameraSpace = 0x62436D53; // bCmS
    public const uint IsFullEnvLight = 0x6246454C; // bFEL
    public const uint IsClipOwnSetting = 0x624F5374; // bOSt
    public const uint NearClipBegin = 0x004E4342; // NCB
    public const uint NearClipEnd = 0x004E4345; // NCE
    public const uint FarClipBegin = 0x00464342; // FCB
    public const uint FarClipEnd = 0x00464345; // FCE
    public const uint SoftParticleFadeRange = 0x53504652; // SPFR
    public const uint SoftKeyOffset = 0x00534B4F; // SKO
    public const uint DrawLayerType = 0x44774C79; // DwLy
    public const uint DrawOrderType = 0x44774F54; // DwOT
    public const uint DirectionalLightSourceType = 0x444C5354; // DLST
    public const uint PointLightsType1 = 0x504C3153; // PL1S
    public const uint PointLightsType2 = 0x504C3253; // PL2S
    public const uint RevisedValuesPosX = 0x52765078; // RvPx
    public const uint RevisedValuesPosY = 0x52765079; // RvPy
    public const uint RevisedValuesPosZ = 0x5276507A; // RvPz
    public const uint RevisedValuesRotX = 0x52765278; // RvRx
    public const uint RevisedValuesRotY = 0x52765279; // RvRy
    public const uint RevisedValuesRotZ = 0x5276527A; // RvRz
    public const uint RevisedValuesScaleX = 0x52765378; // RvSx
    public const uint RevisedValuesScaleY = 0x52765379; // RvSy
    public const uint RevisedValuesScaleZ = 0x5276537A; // RvSz
    public const uint RevisedValuesColorR = 0x00527652; // RvR
    public const uint RevisedValuesColorG = 0x00527647; // RvG
    public const uint RevisedValuesColorB = 0x00527642; // RvB
    public const uint FadeEnabledX = 0x41465865; // AFXe
    public const uint FadeInnerX = 0x41465869; // AFXi
    public const uint FadeOuterX = 0x4146586F; // AFXo
    public const uint FadeEnabledY = 0x41465965; // AFYe
    public const uint FadeInnerY = 0x41465969; // AFYi
    public const uint FadeOuterY = 0x4146596F; // AFYo
    public const uint FadeEnabledZ = 0x41465A65; // AFZe
    public const uint FadeInnerZ = 0x41465A69; // AFZi
    public const uint FadeOuterZ = 0x41465A6F; // AFZo
    public const uint GlobalFogEnabled = 0x62474645; // bGFE
    public const uint GlobalFogInfluence = 0x4746494D; // GFIM
    public const uint LtsEnabled = 0x624C5453; // bLTS
    public const uint NumSchedulers = 0x5363436E; // ScCn
    public const uint NumTimelines = 0x546C436E; // TlCn
    public const uint NumEmitters = 0x456D436E; // EmCn
    public const uint NumParticles = 0x5072436E; // PrCn
    public const uint NumEffectors = 0x4566436E; // EfCn
    public const uint NumBinders = 0x4264436E; // BdCn
    public const uint NumTextures = 0x5478436E; // TxCn
    public const uint NumModels = 0x4D64436E; // MdCn
    public const uint Scheduler = 0x53636864; // Schd
    public const uint Timeline = 0x546D4C6E; // TmLn
    public const uint Emitter = 0x456D6974; // Emit
    public const uint Particle = 0x5074636C; // Ptcl
    public const uint Effector = 0x45666374; // Efct
    public const uint Binder = 0x42696E64; // Bind
    public const uint Texture = 0x00546578; // Tex
    public const uint Model = 0x4D6F646C; // Modl

    /// <summary>Converts a block name into its string representation.</summary>
    public static string ToString(uint name)
    {
        Span<char> chars = stackalloc char[4];
        var length = 0;
        for (var i = 3; i >= 0; i--) {
            var b = (byte) (name >> (i * 8));
            if (b == 0 && length == 0)
                continue;
            chars[length++] = b is >= 0x20 and < 0x7F ? (char) b : '?';
        }

        return new string(chars[..length]);
    }

    /// <summary>Tests whether a value looks like a block name; that is, zero or more leading zero bytes followed by
    /// printable ASCII characters.</summary>
    public static bool IsPlausible(uint name)
    {
        var started = false;
        for (var i = 3; i >= 0; i--) {
            var b = (byte) (name >> (i * 8));
            if (b == 0 && !started)
                continue;
            if (b is < 0x20 or >= 0x7F)
                return false;
            started = true;
        }

        return started;
    }
}