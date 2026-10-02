using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;

/// <summary>Magic values used in TMB (TMLB) timeline files. Stored little endian; read as uint.</summary>
public static class TmbMagic {
    /// <summary>"TMLB"; file header.</summary>
    public const uint Tmlb = 0x424C4D54;

    /// <summary>"TMDH"; timeline header entry.</summary>
    public const uint Tmdh = 0x48444D54;

    /// <summary>"TMPP"; .pap path entry.</summary>
    public const uint Tmpp = 0x50504D54;

    /// <summary>"TMAL"; actor list entry.</summary>
    public const uint Tmal = 0x4C414D54;

    /// <summary>"TMAC"; actor entry.</summary>
    public const uint Tmac = 0x43414D54;

    /// <summary>"TMTR"; track entry.</summary>
    public const uint Tmtr = 0x52544D54;

    /// <summary>"TMFC"; f-curve entry. Not parsed in detail.</summary>
    public const uint Tmfc = 0x43464D54;

    /// <summary>Converts a magic value into its 4-character representation.</summary>
    public static string ToString(uint magic)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++) {
            var b = (byte) (magic >> (i * 8));
            chars[i] = b is >= 0x20 and < 0x7F ? (char) b : '?';
        }

        return new string(chars);
    }

    /// <summary>Tests whether the magic denotes an event entry ("C###"), and extracts the event code.</summary>
    public static bool TryGetEventCode(uint magic, out int code)
    {
        code = 0;
        if ((magic & 0xFF) != 'C')
            return false;

        for (var i = 1; i < 4; i++) {
            var b = (int) ((magic >> (i * 8)) & 0xFF);
            if (b is < '0' or > '9')
                return false;
            code = code * 10 + (b - '0');
        }

        return true;
    }

    /// <summary>Gets a descriptive name for an event code, if known.</summary>
    public static string? GetEventName(int code) => code switch {
        2 => "Timeline Reference",
        6 => "Fly Text",
        9 => "Animation (.pap only)",
        10 => "Animation",
        11 => "Fly Text",
        12 => "VFX",
        14 => "Weapon Position",
        15 => "Weapon Size",
        31 => "Summon Animation",
        33 => "Crafting Delay",
        34 => "Gathering Delay",
        42 => "Footstep",
        43 => "Summon Weapon",
        53 => "Voice Line",
        63 => "Sound",
        67 => "Flinch",
        68 => "Shade Color",
        75 => "Terrain VFX",
        93 => "Color",
        94 => "Invisibility",
        125 => "Animation Lock",
        131 => "Animation Cancelled by Movement",
        142 => "Freeze Position",
        173 => "VFX",
        174 => "Object Control",
        175 => "Object Scaling",
        192 => "Voice Line",
        197 => "Voice Line",
        198 => "Lemure",
        203 => "Summon Weapon Visibility",
        204 => "Reaper Shroud",
        211 => "Lock Facing Direction",
        _ => null,
    };
}