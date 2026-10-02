using System;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;

/// <summary>A "C###" event entry. Events without a dedicated parser are represented using this class.</summary>
public class TmbEventEntry : TmbIdentifiedEntry {
    /// <summary>Event code (the number in "C###").</summary>
    public readonly int Code;

    /// <summary>The body after <see cref="TmbIdentifiedEntry.Id"/> and <see cref="TmbIdentifiedEntry.Time"/>,
    /// interpreted as 32-bit integers.</summary>
    public readonly int[] Values;

    internal TmbEventEntry(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        TmbMagic.TryGetEventCode(this.Magic, out this.Code);
        this.Values = MemoryMarshal.Cast<byte, int>(this.Body.AsSpan(4, (this.Body.Length - 4) & ~3)).ToArray();
    }

    /// <summary>Descriptive name of this event, if known.</summary>
    public string? Name => TmbMagic.GetEventName(this.Code);

    public override string ToString() =>
        this.Name is { } name ? $"{base.ToString()} ({name})" : base.ToString();

    internal static TmbEventEntry Create(ReadOnlySpan<byte> data, int offset, int code, int size) => code switch {
        2 when size >= 28 => new TmbTimelineReferenceEvent(data, offset),
        9 when size >= 24 => new TmbAnimationEvent(data, offset),
        10 when size >= 40 => new TmbAnimationEvent(data, offset),
        12 when size >= 72 => new TmbVfxEvent(data, offset),
        63 when size >= 32 => new TmbSoundEvent(data, offset),
        68 when size >= 36 => new TmbShadeColorEvent(data, offset),
        75 when size >= 64 => new TmbTerrainVfxEvent(data, offset),
        93 when size >= 40 => new TmbColorEvent(data, offset),
        94 when size >= 32 => new TmbInvisibilityEvent(data, offset),
        173 when size >= 68 => new TmbBoundVfxEvent(data, offset),
        _ => new TmbEventEntry(data, offset),
    };
}

/// <summary>C002: plays another timeline (.tmb).</summary>
public sealed class TmbTimelineReferenceEvent : TmbEventEntry {
    public readonly int Duration;
    public readonly int Unknown1;
    public readonly int Unknown2;

    /// <summary>Path as stored in the file, without the extension.</summary>
    public readonly string Path;

    internal TmbTimelineReferenceEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Duration = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.Unknown2 = this.ReadInt32(12);
        this.Path = this.ReadReferencedString(data, 16);
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path + ".tmb";
}

/// <summary>C009 (.pap only) and C010: plays an animation.</summary>
public sealed class TmbAnimationEvent : TmbEventEntry {
    public readonly int Duration;
    public readonly int Unknown1;

    /// <summary>Path as stored in the file, without the extension.</summary>
    public readonly string Path;

    /// <summary>Whether this is a C010 event, in which case the fields below are valid.</summary>
    public readonly bool IsExtended;

    /// <summary>C010 only.</summary>
    public readonly int Flags;

    /// <summary>C010 only.</summary>
    public readonly float AnimationStart;

    /// <summary>C010 only.</summary>
    public readonly float AnimationEnd;

    /// <summary>C010 only.</summary>
    public readonly int Unknown2;

    internal TmbAnimationEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Duration = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        if (this.Code == 10) {
            this.IsExtended = true;
            this.Flags = this.ReadInt32(12);
            this.AnimationStart = this.ReadSingle(16);
            this.AnimationEnd = this.ReadSingle(20);
            this.Path = this.ReadReferencedString(data, 24);
            this.Unknown2 = this.ReadInt32(28);
        } else {
            this.Path = this.ReadReferencedString(data, 12);
        }
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path + ".pap";
}

/// <summary>C012: plays a VFX (.avfx) with transform and color curves.</summary>
public sealed class TmbVfxEvent : TmbEventEntry {
    public readonly int Duration;
    public readonly int Unknown1;

    /// <summary>Path as stored in the file, including the extension.</summary>
    public readonly string Path;

    public readonly short[] BindPoints;
    public readonly float[] Scale;
    public readonly float[] Rotation;
    public readonly float[] Position;
    public readonly float[] Rgba;
    public readonly int Visibility;
    public readonly int Unknown2;

    internal TmbVfxEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Duration = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.Path = this.ReadReferencedString(data, 12);
        this.BindPoints = [this.ReadInt16(16), this.ReadInt16(18), this.ReadInt16(20), this.ReadInt16(22)];
        this.Scale = this.ReadReferencedArray<float>(data, 24, 28);
        this.Rotation = this.ReadReferencedArray<float>(data, 32, 36);
        this.Position = this.ReadReferencedArray<float>(data, 40, 44);
        this.Rgba = this.ReadReferencedArray<float>(data, 48, 52);
        this.Visibility = this.ReadInt32(56);
        this.Unknown2 = this.ReadInt32(60);
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path;
}

/// <summary>C173: plays a VFX (.avfx) attached to bind points.</summary>
public sealed class TmbBoundVfxEvent : TmbEventEntry {
    public readonly int Unknown1;
    public readonly int Unknown2;

    /// <summary>Path as stored in the file, including the extension.</summary>
    public readonly string Path;

    public readonly short BindPoint1;
    public readonly short BindPoint2;

    /// <summary>The 10 integers following the bind points.</summary>
    public readonly int[] Unknowns;

    internal TmbBoundVfxEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Unknown1 = this.ReadInt32(4);
        this.Unknown2 = this.ReadInt32(8);
        this.Path = this.ReadReferencedString(data, 12);
        this.BindPoint1 = this.ReadInt16(16);
        this.BindPoint2 = this.ReadInt16(18);
        this.Unknowns = MemoryMarshal.Cast<byte, int>(this.Body.AsSpan(20, 40)).ToArray();
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path;
}

/// <summary>C063: plays a sound (.scd).</summary>
public sealed class TmbSoundEvent : TmbEventEntry {
    public readonly int Loop;
    public readonly int Interrupt;

    /// <summary>Path as stored in the file, including the extension.</summary>
    public readonly string Path;

    public readonly uint SoundIndex;
    public readonly uint SoundPosition;

    internal TmbSoundEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Loop = this.ReadInt32(4);
        this.Interrupt = this.ReadInt32(8);
        this.Path = this.ReadReferencedString(data, 12);
        this.SoundIndex = this.ReadUInt32(16);
        this.SoundPosition = this.ReadUInt32(20);
    }

    public override string? GamePath => this.Path.Length == 0 ? null : this.Path;
}

/// <summary>C068: shade color.</summary>
public sealed class TmbShadeColorEvent : TmbEventEntry {
    public readonly int Unknown1;
    public readonly int Unknown2;
    public readonly float[] Color1;
    public readonly float[] Color2;

    internal TmbShadeColorEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Unknown1 = this.ReadInt32(4);
        this.Unknown2 = this.ReadInt32(8);
        this.Color1 = this.ReadReferencedArray<float>(data, 12, 16);
        this.Color2 = this.ReadReferencedArray<float>(data, 20, 24);
    }
}

/// <summary>C075: terrain VFX.</summary>
public sealed class TmbTerrainVfxEvent : TmbEventEntry {
    public readonly int Enabled;
    public readonly int Unknown1;
    public readonly int Shape;
    public readonly float[] Scale;
    public readonly float[] Rotation;
    public readonly float[] Position;
    public readonly float[] Rgba;
    public readonly int Unknown2;
    public readonly int Unknown3;

    internal TmbTerrainVfxEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Enabled = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.Shape = this.ReadInt32(12);
        this.Scale = this.ReadReferencedArray<float>(data, 16, 20);
        this.Rotation = this.ReadReferencedArray<float>(data, 24, 28);
        this.Position = this.ReadReferencedArray<float>(data, 32, 36);
        this.Rgba = this.ReadReferencedArray<float>(data, 40, 44);
        this.Unknown2 = this.ReadInt32(48);
        this.Unknown3 = this.ReadInt32(52);
    }
}

/// <summary>C093: color.</summary>
public sealed class TmbColorEvent : TmbEventEntry {
    public readonly int Duration;
    public readonly int Unknown1;
    public readonly float[] Color1;
    public readonly float[] Color2;
    public readonly int Unknown2;

    internal TmbColorEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.Duration = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.Color1 = this.ReadReferencedArray<float>(data, 12, 16);
        this.Color2 = this.ReadReferencedArray<float>(data, 20, 24);
        this.Unknown2 = this.ReadInt32(28);
    }
}

/// <summary>C094: invisibility.</summary>
public sealed class TmbInvisibilityEvent : TmbEventEntry {
    public readonly int FadeTime;
    public readonly int Unknown1;
    public readonly float StartVisibility;
    public readonly float EndVisibility;

    /// <summary>Offset of <see cref="Extra"/>, relative to <see cref="TmbEntry.BodyOffset"/>; 0 if none.</summary>
    public readonly uint ExtraOffset;

    /// <summary>5 integers pointed by <see cref="ExtraOffset"/>, or empty.</summary>
    public readonly int[] Extra;

    internal TmbInvisibilityEvent(ReadOnlySpan<byte> data, int offset) : base(data, offset)
    {
        this.FadeTime = this.ReadInt32(4);
        this.Unknown1 = this.ReadInt32(8);
        this.StartVisibility = this.ReadSingle(12);
        this.EndVisibility = this.ReadSingle(16);
        this.ExtraOffset = this.ReadUInt32(20);
        this.Extra = this.ExtraOffset == 0 ? [] : this.ReadArray<int>(data, this.ExtraOffset, 5);
    }
}