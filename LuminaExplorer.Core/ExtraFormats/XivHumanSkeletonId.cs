﻿namespace LuminaExplorer.Core.ExtraFormats; 

public enum XivHumanSkeletonId : ushort {
    // Note: values are decimal; C# does not support octal literal notation
    NotHuman = 0000,
    HyurMidlanderMale = 0101,
    HyurMidlanderMaleNpc = 0104,
    HyurMidlanderFemale = 0201,
    HyurMidlanderFemaleNpc = 0204,
    HyurHighlanderMale = 0301,
    HyurHighlanderMaleNpc = 0304,
    HyurHighlanderFemale = 0401,
    HyurHighlanderFemaleNpc = 0404,
    ElezenMale = 0501,
    ElezenMaleNpc = 0504,
    ElezenFemale = 0601,
    ElezenFemaleNpc = 0604,
    MiqoteMale = 0701,
    MiqoteMaleNpc = 0704,
    MiqoteFemale = 0801,
    MiqoteFemaleNpc = 0804,
    RoegadynMale = 0901,
    RoegadynMaleNpc = 0904,
    RoegadynFemale = 1001,
    RoegadynFemaleNpc = 1004,
    LalafellMale = 1101,
    LalafellMaleNpc = 1104,
    LalafellFemale = 1201,
    LalafellFemaleNpc = 1204,
    AuraMale = 1301,
    AuraMaleNpc = 1304,
    AuraFemale = 1401,
    AuraFemaleNpc = 1404,
    HrothgarMale = 1501,
    HrothgarMaleNpc = 1504,
    HrothgarFemale = 1601,
    HrothgarFemaleNpc = 1604,
    VieraMale = 1701,
    VieraMaleNpc = 1704,
    VieraFemale = 1801,
    VieraFemaleNpc = 1804,
    NpcMale = 9104,
    NpcFemale = 9204,
    
    DefaultHuman = HyurMidlanderMale,
}
