using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Avfx;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>VFX container. Top-level properties are parsed; sub-objects are exposed as raw blocks.</summary>
/// <remarks>Absent properties are null. Absent components of present vectors are NaN.</remarks>
[FileExtension(".avfx")]
public class AvfxFile : FileResource {
    /// <summary>"AVFX".</summary>
    public const uint MagicValue = AvfxBlockName.Avfx;

    /// <summary>Size of the root block data, as declared in the header.</summary>
    public uint DeclaredSize;

    /// <summary>All top-level blocks, in file order.</summary>
    public List<AvfxBlock> Blocks = new();

    public uint? Version;
    public bool? IsDelayFastParticle;
    public bool? IsFitGround;
    public bool? IsTransformSkip;
    public bool? IsAllStopOnHide;
    public bool? CanBeClippedOut;
    public bool? ClipBoxEnabled;
    public Vector3? ClipBox;
    public Vector3? ClipBoxSize;
    public float? BiasZmaxScale;
    public float? BiasZmaxDistance;
    public bool? IsCameraSpace;
    public bool? IsFullEnvLight;
    public bool? IsClipOwnSetting;
    public float? NearClipBegin;
    public float? NearClipEnd;
    public float? FarClipBegin;
    public float? FarClipEnd;
    public float? SoftParticleFadeRange;
    public float? SoftKeyOffset;
    public uint? DrawLayerType;
    public uint? DrawOrderType;
    public uint? DirectionalLightSourceType;
    public uint? PointLightsType1;
    public uint? PointLightsType2;
    public Vector3? RevisedValuesPos;
    public Vector3? RevisedValuesRot;
    public Vector3? RevisedValuesScale;
    public Vector3? RevisedValuesColor;
    public bool? FadeEnabledX;
    public float? FadeInnerX;
    public float? FadeOuterX;
    public bool? FadeEnabledY;
    public float? FadeInnerY;
    public float? FadeOuterY;
    public bool? FadeEnabledZ;
    public float? FadeInnerZ;
    public float? FadeOuterZ;
    public bool? GlobalFogEnabled;
    public float? GlobalFogInfluence;
    public bool? LtsEnabled;

    public uint? DeclaredSchedulerCount;
    public uint? DeclaredTimelineCount;
    public uint? DeclaredEmitterCount;
    public uint? DeclaredParticleCount;
    public uint? DeclaredEffectorCount;
    public uint? DeclaredBinderCount;
    public uint? DeclaredTextureCount;
    public uint? DeclaredModelCount;

    public List<AvfxBlock> Schedulers = new();
    public List<AvfxBlock> Timelines = new();
    public List<AvfxBlock> Emitters = new();
    public List<AvfxBlock> Particles = new();
    public List<AvfxBlock> Effectors = new();
    public List<AvfxBlock> Binders = new();

    /// <summary>Game paths of textures referenced by index from particles.</summary>
    public List<string> Textures = new();

    public List<AvfxBlock> Models = new();

    public override void LoadFile()
    {
        var data = this.Data.AsSpan();
        if (data.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(data) != MagicValue)
            throw new InvalidDataException("Invalid AVFX magic.");

        this.DeclaredSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var end = (int) Math.Min(8L + this.DeclaredSize, data.Length);

        for (var offset = 8; offset + 8 <= end;) {
            var block = AvfxBlock.Parse(data[..end], offset);
            offset += block.PaddedSize;
            this.Blocks.Add(block);
            this.ApplyBlock(block);
        }
    }

    private void ApplyBlock(AvfxBlock block)
    {
        switch (block.Name) {
            // @formatter:off
            case AvfxBlockName.Version: this.Version = block.ToUInt32(); break;
            case AvfxBlockName.IsDelayFastParticle: this.IsDelayFastParticle = block.ToBoolean(); break;
            case AvfxBlockName.IsFitGround: this.IsFitGround = block.ToBoolean(); break;
            case AvfxBlockName.IsTransformSkip: this.IsTransformSkip = block.ToBoolean(); break;
            case AvfxBlockName.IsAllStopOnHide: this.IsAllStopOnHide = block.ToBoolean(); break;
            case AvfxBlockName.CanBeClippedOut: this.CanBeClippedOut = block.ToBoolean(); break;
            case AvfxBlockName.ClipBoxEnabled: this.ClipBoxEnabled = block.ToBoolean(); break;
            case AvfxBlockName.ClipBoxX: SetX(ref this.ClipBox, block.ToSingle()); break;
            case AvfxBlockName.ClipBoxY: SetY(ref this.ClipBox, block.ToSingle()); break;
            case AvfxBlockName.ClipBoxZ: SetZ(ref this.ClipBox, block.ToSingle()); break;
            case AvfxBlockName.ClipBoxSizeX: SetX(ref this.ClipBoxSize, block.ToSingle()); break;
            case AvfxBlockName.ClipBoxSizeY: SetY(ref this.ClipBoxSize, block.ToSingle()); break;
            case AvfxBlockName.ClipBoxSizeZ: SetZ(ref this.ClipBoxSize, block.ToSingle()); break;
            case AvfxBlockName.BiasZmaxScale: this.BiasZmaxScale = block.ToSingle(); break;
            case AvfxBlockName.BiasZmaxDistance: this.BiasZmaxDistance = block.ToSingle(); break;
            case AvfxBlockName.IsCameraSpace: this.IsCameraSpace = block.ToBoolean(); break;
            case AvfxBlockName.IsFullEnvLight: this.IsFullEnvLight = block.ToBoolean(); break;
            case AvfxBlockName.IsClipOwnSetting: this.IsClipOwnSetting = block.ToBoolean(); break;
            case AvfxBlockName.NearClipBegin: this.NearClipBegin = block.ToSingle(); break;
            case AvfxBlockName.NearClipEnd: this.NearClipEnd = block.ToSingle(); break;
            case AvfxBlockName.FarClipBegin: this.FarClipBegin = block.ToSingle(); break;
            case AvfxBlockName.FarClipEnd: this.FarClipEnd = block.ToSingle(); break;
            case AvfxBlockName.SoftParticleFadeRange: this.SoftParticleFadeRange = block.ToSingle(); break;
            case AvfxBlockName.SoftKeyOffset: this.SoftKeyOffset = block.ToSingle(); break;
            case AvfxBlockName.DrawLayerType: this.DrawLayerType = block.ToUInt32(); break;
            case AvfxBlockName.DrawOrderType: this.DrawOrderType = block.ToUInt32(); break;
            case AvfxBlockName.DirectionalLightSourceType: this.DirectionalLightSourceType = block.ToUInt32(); break;
            case AvfxBlockName.PointLightsType1: this.PointLightsType1 = block.ToUInt32(); break;
            case AvfxBlockName.PointLightsType2: this.PointLightsType2 = block.ToUInt32(); break;
            case AvfxBlockName.RevisedValuesPosX: SetX(ref this.RevisedValuesPos, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesPosY: SetY(ref this.RevisedValuesPos, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesPosZ: SetZ(ref this.RevisedValuesPos, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesRotX: SetX(ref this.RevisedValuesRot, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesRotY: SetY(ref this.RevisedValuesRot, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesRotZ: SetZ(ref this.RevisedValuesRot, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesScaleX: SetX(ref this.RevisedValuesScale, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesScaleY: SetY(ref this.RevisedValuesScale, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesScaleZ: SetZ(ref this.RevisedValuesScale, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesColorR: SetX(ref this.RevisedValuesColor, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesColorG: SetY(ref this.RevisedValuesColor, block.ToSingle()); break;
            case AvfxBlockName.RevisedValuesColorB: SetZ(ref this.RevisedValuesColor, block.ToSingle()); break;
            case AvfxBlockName.FadeEnabledX: this.FadeEnabledX = block.ToBoolean(); break;
            case AvfxBlockName.FadeInnerX: this.FadeInnerX = block.ToSingle(); break;
            case AvfxBlockName.FadeOuterX: this.FadeOuterX = block.ToSingle(); break;
            case AvfxBlockName.FadeEnabledY: this.FadeEnabledY = block.ToBoolean(); break;
            case AvfxBlockName.FadeInnerY: this.FadeInnerY = block.ToSingle(); break;
            case AvfxBlockName.FadeOuterY: this.FadeOuterY = block.ToSingle(); break;
            case AvfxBlockName.FadeEnabledZ: this.FadeEnabledZ = block.ToBoolean(); break;
            case AvfxBlockName.FadeInnerZ: this.FadeInnerZ = block.ToSingle(); break;
            case AvfxBlockName.FadeOuterZ: this.FadeOuterZ = block.ToSingle(); break;
            case AvfxBlockName.GlobalFogEnabled: this.GlobalFogEnabled = block.ToBoolean(); break;
            case AvfxBlockName.GlobalFogInfluence: this.GlobalFogInfluence = block.ToSingle(); break;
            case AvfxBlockName.LtsEnabled: this.LtsEnabled = block.ToBoolean(); break;
            case AvfxBlockName.NumSchedulers: this.DeclaredSchedulerCount = block.ToUInt32(); break;
            case AvfxBlockName.NumTimelines: this.DeclaredTimelineCount = block.ToUInt32(); break;
            case AvfxBlockName.NumEmitters: this.DeclaredEmitterCount = block.ToUInt32(); break;
            case AvfxBlockName.NumParticles: this.DeclaredParticleCount = block.ToUInt32(); break;
            case AvfxBlockName.NumEffectors: this.DeclaredEffectorCount = block.ToUInt32(); break;
            case AvfxBlockName.NumBinders: this.DeclaredBinderCount = block.ToUInt32(); break;
            case AvfxBlockName.NumTextures: this.DeclaredTextureCount = block.ToUInt32(); break;
            case AvfxBlockName.NumModels: this.DeclaredModelCount = block.ToUInt32(); break;
            case AvfxBlockName.Scheduler: this.Schedulers.Add(block); break;
            case AvfxBlockName.Timeline: this.Timelines.Add(block); break;
            case AvfxBlockName.Emitter: this.Emitters.Add(block); break;
            case AvfxBlockName.Particle: this.Particles.Add(block); break;
            case AvfxBlockName.Effector: this.Effectors.Add(block); break;
            case AvfxBlockName.Binder: this.Binders.Add(block); break;
            case AvfxBlockName.Texture: this.Textures.Add(block.ToStringValue()); break;
            case AvfxBlockName.Model: this.Models.Add(block); break;
            // @formatter:on
        }
    }

    private static void SetX(ref Vector3? v, float value) => v = (v ?? new Vector3(float.NaN)) with { X = value };

    private static void SetY(ref Vector3? v, float value) => v = (v ?? new Vector3(float.NaN)) with { Y = value };

    private static void SetZ(ref Vector3? v, float value) => v = (v ?? new Vector3(float.NaN)) with { Z = value };
}