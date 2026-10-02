namespace LuminaExplorer.Core.Audio;

/// <summary>Codec identifiers used in SCD audio entries.</summary>
public enum ScdAudioCodec : uint {
    Pcm16 = 0x01,
    PsAdpcm = 0x03,
    OggVorbis = 0x06,
    Mp3 = 0x07,
    NintendoDspAdpcm = 0x0A,
    Xma2 = 0x0B,
    MsAdpcm = 0x0C,
    Atrac3 = 0x0E,
    Atrac9 = 0x16,

    /// <summary>CRI HCA. The stream header ("HCA\0") lives in the extra data area.</summary>
    CriHca = 0x1A,

    Empty = 0xFFFFFFFF,
}

public static class ScdAudioCodecExtensions {
    public static string GetDisplayName(this ScdAudioCodec codec) => codec switch {
        ScdAudioCodec.Pcm16 => "PCM",
        ScdAudioCodec.PsAdpcm => "PS-ADPCM",
        ScdAudioCodec.OggVorbis => "Ogg Vorbis",
        ScdAudioCodec.Mp3 => "MP3",
        ScdAudioCodec.NintendoDspAdpcm => "DSP-ADPCM",
        ScdAudioCodec.Xma2 => "XMA2",
        ScdAudioCodec.MsAdpcm => "MS-ADPCM",
        ScdAudioCodec.Atrac3 => "ATRAC3",
        ScdAudioCodec.Atrac9 => "ATRAC9",
        ScdAudioCodec.CriHca => "CRI HCA",
        ScdAudioCodec.Empty => "(empty)",
        _ => $"Unknown (0x{(uint) codec:X2})",
    };
}
