using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.QuaternionTrack;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation.Vector3Track;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation;

public class AnimationSet : IAnimation {
    public readonly ImmutableList<AnimationBlock> Blocks;
    public readonly float BlockDuration;
    public readonly float FrameDuration;

    private readonly ConcatAnimation _concatAnimation;

    public AnimationSet(
        ImmutableList<AnimationBlock> blocks,
        float duration,
        float blockDuration,
        float frameDuration)
    {
        this.Blocks = blocks;
        this.BlockDuration = blockDuration;
        this.FrameDuration = frameDuration;
        this.Duration = duration;
        this._concatAnimation = new(blocks);
    }

    public float Duration { get; }
    public ImmutableSortedSet<int> AffectedBoneIndices => this._concatAnimation.AffectedBoneIndices;
    public IVector3Track Translation(int boneIndex) => this._concatAnimation.Translation(boneIndex);
    public IQuaternionTrack Rotation(int boneIndex) => this._concatAnimation.Rotation(boneIndex);
    public IVector3Track Scale(int boneIndex) => this._concatAnimation.Scale(boneIndex);

    public static AnimationSet Decode(Node animationBinding)
    {
        if (!animationBinding.AsMap.TryGetValue("animation", out var v) || v is not ValueNode v2)
            throw new NotSupportedException("Animation binding has no animation.");

        if (!animationBinding.AsMap.TryGetValue("transformTrackToBoneIndices", out var v3) ||
            v3 is not ValueArray v4 ||
            v4.InnerType.ElementType != FieldElementType.Integer)
            throw new NotSupportedException("Animation binding has no transformTrackToBoneIndices.");

        var transformTrackToBoneIndices = v4.Values.Select(x => ((ValueInt) x!).Value).ToImmutableList();
        return v2.Node.Definition.Name switch {
            "hkaSplineCompressedAnimation" => Decode(v2.Node, transformTrackToBoneIndices),
            "hkaInterleavedUncompressedAnimation" => DecodeInterleaved(v2.Node, transformTrackToBoneIndices),
            var name => throw new NotSupportedException($"Animation type {name} is not supported."),
        };
    }

    public static AnimationSet DecodeInterleaved(
        Node iua,
        ImmutableList<int> transformTrackToBoneIndices)
    {
        var duration = (iua.AsMap.GetValueOrDefault("duration") as ValueFloat)?.Value ?? 0f;
        var numberOfTransformTracks = (iua.AsMap.GetValueOrDefault("numberOfTransformTracks") as ValueInt)?.Value ?? 0;
        var transforms = (iua.AsMap.GetValueOrDefault("transforms") as ValueArray)?.Values ?? [];

        // Each item is a hkQsTransform: translation (xyzw), rotation (xyzw), scale (xyzw); stored as frame-major.
        var numFrames = numberOfTransformTracks == 0 ? 0 : transforms.Count / numberOfTransformTracks;
        if (numberOfTransformTracks > 0 && numFrames == 0)
            throw new InvalidDataException("Interleaved animation has no frames.");

        var frameDuration = numFrames > 1 ? duration / (numFrames - 1) : duration;
        var tracks = new List<AnimationTrack>(numberOfTransformTracks);
        for (var track = 0; track < numberOfTransformTracks; track++) {
            var translations = new Vector3[numFrames];
            var rotations = new Quaternion[numFrames];
            var scales = new Vector3[numFrames];
            for (var frame = 0; frame < numFrames; frame++) {
                var f = ((ValueArray) transforms[frame * numberOfTransformTracks + track]!).Values;
                var x = new float[12];
                for (var i = 0; i < 12 && i < f.Count; i++)
                    x[i] = (f[i] as ValueFloat)?.Value ?? 0f;
                translations[frame] = new(x[0], x[1], x[2]);
                rotations[frame] = new(x[4], x[5], x[6], x[7]);
                scales[frame] = new(x[8], x[9], x[10]);
            }

            tracks.Add(
                new(
                    translations.All(t => t == translations[0])
                        ? new StaticVector3Track(translations[0], duration, false)
                        : new SampledVector3Track(translations, duration, frameDuration),
                    rotations.All(r => r == rotations[0])
                        ? new StaticQuaternionTrack(rotations[0], duration, false)
                        : new SampledQuaternionTrack(rotations, duration, frameDuration),
                    scales.All(s => s == scales[0])
                        ? new StaticVector3Track(scales[0], duration, false)
                        : new SampledVector3Track(scales, duration, frameDuration)));
        }

        return new(
            [new(duration, tracks.ToImmutableList(), transformTrackToBoneIndices)],
            duration,
            duration,
            frameDuration);
    }

    public static AnimationSet Decode(
        Node sca,
        ImmutableList<int> transformTrackToBoneIndices)
    {
        var res = new List<AnimationBlock>();

        var numFrames = ((ValueInt) sca.AsMap["numFrames"]!).Value;
        var maxFramesPerBlock = ((ValueInt) sca.AsMap["maxFramesPerBlock"]!).Value;
        var numberOfTransformTracks = ((ValueInt) sca.AsMap["numberOfTransformTracks"]!).Value;

        var duration = ((ValueFloat) sca.AsMap["duration"]!).Value;
        var frameDuration = ((ValueFloat) sca.AsMap["frameDuration"]!).Value;
        var maxBlockDuration = ((ValueFloat) sca.AsMap["blockDuration"]!).Value;

        var blockOffsets = ((ValueArray) sca.AsMap["blockOffsets"]!).Values
            .Select(x => ((ValueInt) x!).Value).ToArray();
        var data = ((ValueArray) sca.AsMap["data"]!).Values
            .Select(x => ((ValueByte) x!).Value).ToArray();

        var numPendingFrames = numFrames;
        var pendingDuration = duration;
        foreach (var blockOffset in blockOffsets) {
            using var reader = new BinaryReader(new MemoryStream(data, blockOffset, data.Length - blockOffset));

            var masks = Enumerable.Range(0, numberOfTransformTracks)
                .Select(_ => new TransformMask(reader))
                .ToArray();

            var numBlockFrames = Math.Min(numPendingFrames, maxFramesPerBlock);
            var blockDuration = Math.Min(pendingDuration, maxBlockDuration);
            numPendingFrames -= numBlockFrames;
            pendingDuration -= blockDuration;
            var tracks = new List<AnimationTrack>();
            foreach (var mask in masks) {
                var translations = VectorTrackFromSplineData(
                    reader,
                    mask.Translation,
                    mask.TranslationQuantization,
                    numBlockFrames,
                    frameDuration,
                    blockDuration,
                    false);
                reader.WithAlign(4);

                var rotations = QuaternionTrackFromSplineData(
                    reader,
                    mask.Rotation,
                    mask.RotationQuantization,
                    numBlockFrames,
                    frameDuration,
                    blockDuration);
                reader.WithAlign(4);

                var scales = VectorTrackFromSplineData(
                    reader,
                    mask.Scale,
                    mask.ScaleQuantization,
                    numBlockFrames,
                    frameDuration,
                    blockDuration,
                    true);
                reader.WithAlign(4);

                tracks.Add(new(translations, rotations, scales));
            }

            res.Add(new(blockDuration, tracks.ToImmutableList(), transformTrackToBoneIndices));
        }

        return new(res.ToImmutableList(), duration, maxBlockDuration, frameDuration);
    }

    public static IVector3Track VectorTrackFromSplineData(
        BinaryReader reader,
        VectorType vt,
        ScalarQuantization quantType,
        int numFrames,
        float frameDuration,
        float blockDuration,
        bool isScale)
    {
        if (vt.Spline()) {
            reader.ReadInto(out ushort numItems);
            reader.ReadInto(out byte degree);
            var knots = reader.ReadBytes(numItems + degree + 2);
            reader.WithAlign(4);

            float minx = 0, maxx = 0, miny = 0, maxy = 0, minz = 0, maxz = 0;
            float staticx = 0, staticy = 0, staticz = 0;
            if (vt.SplineX()) {
                reader.ReadInto(out minx);
                reader.ReadInto(out maxx);
            } else if (vt.StaticX()) {
                reader.ReadInto(out staticx);
            }

            if (vt.SplineY()) {
                reader.ReadInto(out miny);
                reader.ReadInto(out maxy);
            } else if (vt.StaticY()) {
                reader.ReadInto(out staticy);
            }

            if (vt.SplineZ()) {
                reader.ReadInto(out minz);
                reader.ReadInto(out maxz);
            } else if (vt.StaticZ()) {
                reader.ReadInto(out staticz);
            }

            var translationControlPoints = new List<float[]>();
            for (var i = 0; i <= numItems; i++) {
                // yes, "<="
                var position = new float[3];
                switch (quantType) {
                    case ScalarQuantization.K8Bit:
                        if (vt.SplineX())
                            position[0] = reader.ReadByte() / (float) byte.MaxValue;
                        if (vt.SplineY())
                            position[1] = reader.ReadByte() / (float) byte.MaxValue;
                        if (vt.SplineZ())
                            position[2] = reader.ReadByte() / (float) byte.MaxValue;
                        break;

                    case ScalarQuantization.K16Bit:
                        if (vt.SplineX())
                            position[0] = reader.ReadUInt16() / (float) ushort.MaxValue;
                        if (vt.SplineY())
                            position[1] = reader.ReadUInt16() / (float) ushort.MaxValue;
                        if (vt.SplineZ())
                            position[2] = reader.ReadUInt16() / (float) ushort.MaxValue;
                        break;

                    default:
                        throw new NotSupportedException();
                }

                position[0] = vt.SplineX() ? minx + (maxx - minx) * position[0] : staticx;
                position[1] = vt.SplineY() ? miny + (maxy - miny) * position[1] : staticy;
                position[2] = vt.SplineZ() ? minz + (maxz - minz) * position[2] : staticz;
                translationControlPoints.Add(position);
            }

            return new SplineVector3Track(
                new(3, translationControlPoints, knots, degree),
                blockDuration,
                numFrames - 1,
                frameDuration);
        }

        if (vt.Static()) {
            return new StaticVector3Track(
                new(
                    vt.StaticX() ? reader.ReadSingle() : (isScale ? 1f : 0f),
                    vt.StaticY() ? reader.ReadSingle() : (isScale ? 1f : 0f),
                    vt.StaticZ() ? reader.ReadSingle() : (isScale ? 1f : 0f)
                ),
                blockDuration,
                false);
        }

        return new StaticVector3Track(
            isScale ? Vector3.One : Vector3.Zero,
            blockDuration,
            true);
    }

    public static IQuaternionTrack QuaternionTrackFromSplineData(
        BinaryReader reader,
        QuaternionType qt,
        QuaternionQuantization quantType,
        int numFrames,
        float frameDuration,
        float blockDuration)
    {
        if (qt.Spline()) {
            reader.ReadInto(out ushort numItems);
            reader.ReadInto(out byte degree);
            var knots = reader.ReadBytes(numItems + degree + 2);

            reader.WithAlign(
                quantType switch {
                    QuaternionQuantization.Quat32 => 4,
                    QuaternionQuantization.Quat48 => 2,
                    _ => 1,
                });

            var rotationControlPoints = new List<float[]>();
            for (var i = 0; i <= numItems; ++i) {
                var rotation = quantType switch {
                    QuaternionQuantization.Quat32 => reader.ReadHk32BitQuaternion(),
                    QuaternionQuantization.Quat40 => reader.ReadHk40BitQuaternion(),
                    QuaternionQuantization.Quat48 => reader.ReadHk48BitQuaternion(),
                    _ => throw new NotSupportedException(),
                };

                rotationControlPoints.Add([rotation.X, rotation.Y, rotation.Z, rotation.W]);
            }

            return new SplineQuaternionTrack(
                new(4, rotationControlPoints, knots, degree),
                blockDuration,
                numFrames - 1,
                frameDuration);
        }

        if (qt.Static()) {
            return new StaticQuaternionTrack(
                quantType switch {
                    QuaternionQuantization.Quat32 => reader.ReadHk32BitQuaternion(),
                    QuaternionQuantization.Quat40 => reader.ReadHk40BitQuaternion(),
                    QuaternionQuantization.Quat48 => reader.ReadHk48BitQuaternion(),
                    _ => throw new NotSupportedException(),
                },
                blockDuration,
                false);
        }

        return new StaticQuaternionTrack(Quaternion.Identity, blockDuration, true);
    }
}
