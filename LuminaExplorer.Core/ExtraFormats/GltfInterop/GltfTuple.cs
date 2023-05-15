using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using DirectN;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing;
using Lumina.Data.Structs;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;
using LuminaExplorer.Core.Util;
using Newtonsoft.Json;
using WicNet;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop;

public class GltfTuple {
    public const uint GlbMagic = 0x46546C67;
    public const uint GlbJsonMagic = 0x4E4F534A;
    public const uint GlbDataMagic = 0x004E4942;

    public readonly GltfRoot Root;
    public readonly MemoryStream DataStream;

    public GltfTuple() {
        Root = new();
        DataStream = new();
        Root.Buffers.Add(new());
        Root.Scene = Root.Scenes.AddAndGetIndex(new());
    }

    public GltfTuple(Stream glbStream, bool leaveOpen = false) {
        using var lbr = new LuminaBinaryReader(glbStream, Encoding.UTF8, leaveOpen, PlatformId.Win32);
        if (lbr.ReadUInt32() != GlbMagic)
            throw new InvalidDataException("Not a glb file.");
        if (lbr.ReadInt32() != 2)
            throw new InvalidDataException("Currently a glb file may only have exactly 2 entries.");
        if (glbStream.Length < lbr.ReadInt32())
            throw new InvalidDataException("File is truncated.");

        var jsonLength = lbr.ReadInt32();
        if (lbr.ReadUInt32() != GlbJsonMagic)
            throw new InvalidDataException("First entry must be a JSON file.");

        Root = JsonConvert.DeserializeObject<GltfRoot>(lbr.ReadFString(jsonLength))
            ?? throw new InvalidDataException("JSON was empty.");

        var dataLength = lbr.ReadInt32();
        if (lbr.ReadUInt32() != GlbDataMagic)
            throw new InvalidDataException("Second entry must be a data file.");

        DataStream = new();
        DataStream.SetLength(dataLength);
        glbStream.ReadExactly(DataStream.GetBuffer().AsSpan(0, dataLength));
    }

    public ReadOnlySpan<byte> Data => DataStream.GetBuffer().AsSpan(0, (int) DataStream.Length);

    public void Compile(Stream target) {
        Root.Buffers[0].ByteLength = DataStream.Length;

        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Root));
        if (json.Length % 4 != 0) {
            var rv = new byte[(json.Length + 3) / 4 * 4];
            Buffer.BlockCopy(json, 0, rv, 0, json.Length);
            for (var i = json.Length; i < rv.Length; i++)
                rv[i] = 0x20; // space
            json = rv;
        }

        using var writer = new NativeWriter(target, Encoding.UTF8, true) {IsBigEndian = false};
        writer.Write(GlbMagic);
        writer.Write(2);
        writer.Write(checked(12 + 8 + json.Length + 8 + (int) DataStream.Length));
        writer.Write(json.Length);
        writer.Write(GlbJsonMagic);
        writer.Write(json);

        writer.Write(checked((int) DataStream.Length));
        writer.Write(GlbDataMagic);
        DataStream.Position = 0;
        DataStream.CopyTo(target);
    }

    public unsafe int AddBufferView<T>(
        string? baseName,
        GltfBufferViewTarget? target,
        ReadOnlySpan<T> data)
        where T : unmanaged {
        var byteLength = sizeof(T) * data.Length;
        var index = Root.BufferViews.AddAndGetIndex(new() {
            Name = baseName is null ? null : $"{baseName}/bufferView",
            ByteOffset = checked((int) (DataStream.Position = (DataStream.Length + 3) / 4 * 4)),
            ByteLength = byteLength,
            Target = target,
        });

        fixed (void* src = data)
            DataStream.Write(new((byte*) src, byteLength));

        return index;
    }

    public int AddAccessor<T>(
        string? baseName,
        Span<T> data,
        int start = 0,
        int count = int.MaxValue,
        int? bufferView = null,
        GltfBufferViewTarget? target = null)
        where T : unmanaged => AddAccessor(baseName, (ReadOnlySpan<T>) data, start, count, bufferView, target);

    public unsafe int AddAccessor<T>(
        string? baseName,
        ReadOnlySpan<T> data,
        int start = 0,
        int count = int.MaxValue,
        int? bufferView = null,
        GltfBufferViewTarget? target = null)
        where T : unmanaged {
        bufferView ??= AddBufferView(baseName, target, data);

        {
            var (componentType, type) = typeof(T) switch {
                var t when t == typeof(byte) => (GltfAccessorComponentTypes.u8, GltfAccessorTypes.Scalar),
                var t when t == typeof(sbyte) => (GltfAccessorComponentTypes.s8, GltfAccessorTypes.Scalar),
                var t when t == typeof(ushort) => (GltfAccessorComponentTypes.u16, GltfAccessorTypes.Scalar),
                var t when t == typeof(short) => (GltfAccessorComponentTypes.s16, GltfAccessorTypes.Scalar),
                var t when t == typeof(uint) => (GltfAccessorComponentTypes.u32, GltfAccessorTypes.Scalar),
                var t when t == typeof(float) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Scalar),
                var t when t == typeof(Quaternion) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Vec4),
                var t when t == typeof(Vector2) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Vec2),
                var t when t == typeof(Vector3) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Vec3),
                var t when t == typeof(Vector4) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Vec4),
                var t when t == typeof(Matrix4x4) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Mat4),
                var t when t == typeof(TypedVec4<byte>) => (GltfAccessorComponentTypes.u8, GltfAccessorTypes.Vec4),
                var t when t == typeof(TypedVec4<sbyte>) => (GltfAccessorComponentTypes.s8, GltfAccessorTypes.Vec4),
                var t when t == typeof(TypedVec4<ushort>) => (GltfAccessorComponentTypes.u16, GltfAccessorTypes.Vec4),
                var t when t == typeof(TypedVec4<short>) => (GltfAccessorComponentTypes.s16, GltfAccessorTypes.Vec4),
                var t when t == typeof(TypedVec4<uint>) => (GltfAccessorComponentTypes.u32, GltfAccessorTypes.Vec4),
                var t when t == typeof(TypedVec4<float>) => (GltfAccessorComponentTypes.f32, GltfAccessorTypes.Vec4),
                _ => throw new NotSupportedException(),
            };

            var componentCount = type switch {
                GltfAccessorTypes.Scalar => 1,
                GltfAccessorTypes.Vec2 => 2,
                GltfAccessorTypes.Vec3 => 3,
                GltfAccessorTypes.Vec4 => 4,
                GltfAccessorTypes.Mat2 => 4,
                GltfAccessorTypes.Mat3 => 9,
                GltfAccessorTypes.Mat4 => 16,
                _ => throw new NotSupportedException(),
            };

            if (count == int.MaxValue)
                count = data.Length - start;

            Tuple<float[], float[]> MinMax<TComponent>(ReadOnlySpan<T> data2)
                where TComponent : unmanaged, INumber<TComponent> {
                var mins = new float[componentCount];
                var maxs = new float[componentCount];
                fixed (void* pData = data2) {
                    var span = new ReadOnlySpan<TComponent>(
                        (TComponent*) ((T*) pData + start),
                        count * componentCount);
                    for (var i = 0; i < componentCount; i++) {
                        var min = span[i];
                        var max = span[i];
                        for (var j = 1; j < count; j++) {
                            var v = span[j * componentCount + i];
                            if (v < min)
                                min = v;
                            if (v > max)
                                max = v;
                        }

                        mins[i] = Convert.ToSingle(min);
                        maxs[i] = Convert.ToSingle(max);
                    }
                }

                return Tuple.Create(mins, maxs);
            }

            var accessor = new GltfAccessor {
                Name = baseName is null ? null : $"{baseName}/accessor[{start}..{start + count}]",
                ByteOffset = start * sizeof(T),
                BufferView = bufferView.Value,
                ComponentType = componentType,
                Count = count,
                Type = type,
                Min = count == 0 ? null : new float[componentCount],
                Max = count == 0 ? null : new float[componentCount],
            };

            (accessor.Min, accessor.Max) = componentType switch {
                GltfAccessorComponentTypes.s8 => MinMax<byte>(data),
                GltfAccessorComponentTypes.u8 => MinMax<sbyte>(data),
                GltfAccessorComponentTypes.s16 => MinMax<short>(data),
                GltfAccessorComponentTypes.u16 => MinMax<ushort>(data),
                GltfAccessorComponentTypes.u32 => MinMax<int>(data),
                GltfAccessorComponentTypes.f32 => MinMax<float>(data),
                _ => throw new NotSupportedException(),
            };

            return Root.Accessors.AddAndGetIndex(accessor);
        }
    }

    public void AddToScene(int meshIndex, int? skinIndex) =>
        Root.Scenes[Root.Scene].Nodes.Add(Root.Nodes.AddAndGetIndex(new() {
            Skin = skinIndex,
            Mesh = meshIndex,
            Children = skinIndex is null ? new() : new() {Root.Skins[skinIndex.Value].Joints[0]},
        }));

    public int? FindMaterial(string mtrlFilePath) {
        var name = Path.GetFileNameWithoutExtension(mtrlFilePath);
        for (var i = 0; i < Root.Materials.Count; i++)
            if (Root.Materials[i].Name == name)
                return i;
        return null;
    }

    public int AttachSkin(params SklbFile[] sklbFiles) {
        var bones = new SklbFile.BoneList();
        foreach (var sklb in sklbFiles)
            bones.AddBones(sklb.Bones);
        var firstGltfNodeIndex = Root.Nodes.AddRangeAndGetIndex(bones.Bones.Select(bone => new GltfNode {
            Name = bone.Name,
            Children = bone.Children.Select(x => x.Index).ToList(),
            Translation = bone.Translation.ToFloatList(Vector3.Zero, 1e-6f),
            Rotation = Quaternion.Normalize(bone.Rotation).ToFloatList(Quaternion.Identity, 1e-6f),
            Scale = bone.Scale.ToFloatList(Vector3.One, 1e-6f),
        }).ToArray());

        return Root.Skins.AddAndGetIndex(new() {
            InverseBindMatrices = AddAccessor(
                null,
                bones.Bones.Select(x => x.BindPoseAbsoluteInverse.Normalize())
                    .ToArray()
                    .AsSpan()),
            Joints = Root.Nodes.Select((_, i) => firstGltfNodeIndex + i).ToList(),
            Extras = new() {
                Alph = sklbFiles.ToDictionary(
                    x => x.FilePath.Path,
                    x => x.AlphData.ToDictionary(y => y.Unk, y => y.Bones.Select(z => z.Name).ToList())),
                Indices = sklbFiles.ToDictionary(
                    x => x.FilePath.Path,
                    x => x.Bones.Select(y => bones.GetRemappedBoneIndex(y)).ToList()),
            },
        });
    }

    public int AttachTexture(string name, WicBitmapSource wicBitmapSource) {
        for (var i = 0; i < Root.Textures.Count; i++)
            if (Root.Textures[i].Name == name)
                return i;

        using var png = new MemoryStream();
        wicBitmapSource.Save(png, WicCodec.GUID_ContainerFormatPng);

        return Root.Textures.AddAndGetIndex(new() {
            Name = name,
            Source = Root.Images.AddAndGetIndex(new() {
                Name = Path.ChangeExtension(name, ".png"),
                MimeType = "image/png",
                BufferView = AddBufferView(
                    name + ".png",
                    null,
                    new ReadOnlySpan<byte>(png.GetBuffer(), 0, (int) png.Length)),
            }),
        });
    }

    public int AttachTexture(string name, DdsFile ddsFile) {
        for (var i = 0; i < Root.Textures.Count; i++)
            if (Root.Textures[i].Name == name)
                return i;

        using var png = new MemoryStream();
        using (var wicBitmapSource = ddsFile.ToWicBitmapSource(0, 0, 0))
            wicBitmapSource.Save(png, WicCodec.GUID_ContainerFormatPng);

        Root.ExtensionsUsed.Add("MSFT_texture_dds");
        return Root.Textures.AddAndGetIndex(new() {
            Name = name,
            Source = Root.Images.AddAndGetIndex(new() {
                Name = Path.ChangeExtension(name, ".png"),
                MimeType = "image/png",
                BufferView = AddBufferView(
                    name + ".png",
                    null,
                    new ReadOnlySpan<byte>(png.GetBuffer(), 0, (int) png.Length)),
            }),
            Extensions = new() {
                MsftTextureDds = new() {
                    Source = Root.Images.AddAndGetIndex(new() {
                        Name = Path.ChangeExtension(name, ".dds"),
                        MimeType = "image/vnd-ms.dds",
                        BufferView = AddBufferView(name + ".dds", null, ddsFile.Data),
                    }),
                },
            },
        });
    }

    public async Task<int> AttachMaterial(Material xivMaterial, Func<string, Task<TexFile?>> texFileGetter) {
        var name = Path.GetFileNameWithoutExtension(xivMaterial.MaterialPath);

        for (var i = 0; i < Root.Materials.Count; i++)
            if (Root.Materials[i].Name == name)
                return i;

        var material = new GltfMaterial {
            Name = name,
            Extras = new() {
                ShaderPack = xivMaterial.ShaderPack,
                VariantId = xivMaterial.VariantId,
                UvColorSets = xivMaterial.File!.UvColorSets.Select(x => new GltfMaterialExtras.ColorSetWithString {
                    Index = x.Index,
                    Unknown1 = x.Unknown1,
                    Name = xivMaterial.File.Strings.AsSpan(x.NameOffset).ExtractCString(),
                }).ToList(),
                ColorSets = xivMaterial.File.ColorSets.Select(x => new GltfMaterialExtras.ColorSetWithString {
                    Index = x.Index,
                    Unknown1 = x.Unknown1,
                    Name = xivMaterial.File.Strings.AsSpan(x.NameOffset).ExtractCString(),
                }).ToList(),
                ShaderKeys = xivMaterial.File.ShaderKeys.ToList(),
                Constants = xivMaterial.File.Constants.ToList(),
                ShaderValues = xivMaterial.File.ShaderValues.ToList(),
            },
        };

        unsafe {
            if (xivMaterial.File.ColorSetInfo is var csi) {
                var colorSetInfoBytes = new ushort[256];
                for (var i = 0; i < 256; i++)
                    colorSetInfoBytes[i] = csi.Data[i];
                material.Extras.ColorSetInfo = colorSetInfoBytes;
            }

            if (xivMaterial.File.ColorSetDyeInfo is var csdi) {
                var colorSetDyeInfoBytes = new ushort[16];
                for (var i = 0; i < 16; i++)
                    colorSetDyeInfoBytes[i] = csdi.Data[i];
                material.Extras.ColorSetInfo = colorSetDyeInfoBytes;
            }
        }

        var texDict = new Dictionary<TextureUsage, Tuple<WicBitmapSource, int?>>();
        try {
            foreach (var (t, s) in xivMaterial.Textures.Zip(xivMaterial.File!.Samplers)) {
                var texFile = t.TexturePath == "dummy.tex" ? null : await texFileGetter(t.TexturePath);
                var ddsFile = texFile?.ToDdsFile();
                int? textureIndexNullable = ddsFile is null ? null : AttachTexture(t.TexturePath, ddsFile);

                if (ddsFile is not null)
                    texDict[t.TextureUsageRaw] = Tuple.Create(ddsFile.ToWicBitmapSource(0, 0, 0), textureIndexNullable);

                material.Extras.Samplers ??= new();
                material.Extras.Samplers.Add(new() {
                    Flags = s.Flags,
                    TextureUsage = t.TextureUsageRaw,
                    TexturePath = t.TexturePath,
                    TextureIndex = textureIndexNullable,
                });
            }

            // Build textures for display purposes.
            if (xivMaterial.ShaderPack == "character.shpk") {
                if (texDict.TryGetValue(TextureUsage.SamplerNormal, out var normalBitmapSourceAndIndex)) {
                    var normalBitmapSource = normalBitmapSourceAndIndex.Item1;
                    normalBitmapSource.ConvertTo(WicPixelFormat.GUID_WICPixelFormat32bppBGRA);

                    WicBitmapSource? diffuseBitmapSource = null;
                    WicBitmapSource? specularBitmapSource = null;
                    WicBitmapSource? emissionBitmapSource = null;
                    try {
                        diffuseBitmapSource = new(
                            normalBitmapSource.Width,
                            normalBitmapSource.Height,
                            normalBitmapSource.PixelFormat);
                        specularBitmapSource = new(
                            normalBitmapSource.Width,
                            normalBitmapSource.Height,
                            normalBitmapSource.PixelFormat);
                        emissionBitmapSource = new(
                            normalBitmapSource.Width,
                            normalBitmapSource.Height,
                            normalBitmapSource.PixelFormat);

                        using (var normalBitmap = normalBitmapSource.AsBitmap())
                        using (var diffuseBitmap = diffuseBitmapSource.AsBitmap())
                        using (var specularBitmap = specularBitmapSource.AsBitmap())
                        using (var emissionBitmap = emissionBitmapSource.AsBitmap())
                        using (var normalLock = normalBitmap.Lock(
                                   WICBitmapLockFlags.WICBitmapLockRead | WICBitmapLockFlags.WICBitmapLockWrite))
                        using (var diffuseLock = diffuseBitmap.Lock(WICBitmapLockFlags.WICBitmapLockWrite))
                        using (var specularLock = specularBitmap.Lock(WICBitmapLockFlags.WICBitmapLockWrite))
                        using (var emissionLock = emissionBitmap.Lock(WICBitmapLockFlags.WICBitmapLockWrite)) {
                            var setInfo = xivMaterial.File!.ColorSetInfo;

                            unsafe void Blend() {
                                normalLock.Object.GetDataPointer(out var bufferSize, out var pbData).ThrowOnError();
                                var normal = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                diffuseLock.Object.GetDataPointer(out bufferSize, out pbData).ThrowOnError();
                                var diffuse = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                specularLock.Object.GetDataPointer(out bufferSize, out pbData).ThrowOnError();
                                var specular = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                emissionLock.Object.GetDataPointer(out bufferSize, out pbData).ThrowOnError();
                                var emission = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                for (var i = 0; i < normal.Length; i++) {
                                    var setIndex1 = (normal[i].a / 17) * 16;
                                    var blendRatio = (normal[i].a % 17) / 17f;
                                    var colorSetIndexT2 = (normal[i].a / 17);
                                    var setIndex2 = (colorSetIndexT2 >= 15 ? 15 : colorSetIndexT2 + 1) * 16;

                                    diffuse[i] = ColorSetBlender.Blend(setInfo, setIndex1, setIndex2, normal[i].b,
                                        blendRatio);
                                    specular[i] = ColorSetBlender.Blend(setInfo, setIndex1, setIndex2, 255, blendRatio);
                                    emission[i] = ColorSetBlender.Blend(setInfo, setIndex1, setIndex2, 255, blendRatio);
                                    normal[i].b = normal[i].a = 255;
                                }
                            }

                            Blend();
                        }

                        if (texDict.TryAdd(
                                TextureUsage.SamplerDiffuse,
                                Tuple.Create(diffuseBitmapSource, (int?) null)))
                            diffuseBitmapSource = null;
                        if (texDict.TryAdd(
                                TextureUsage.SamplerSpecular,
                                Tuple.Create(specularBitmapSource, (int?) null)))
                            specularBitmapSource = null;
                        if (texDict.TryAdd(
                                TextureUsage.SamplerReflection,
                                Tuple.Create(emissionBitmapSource, (int?) null)))
                            emissionBitmapSource = null;
                    } finally {
                        _ = SafeDispose.OneAsync(ref diffuseBitmapSource);
                        _ = SafeDispose.OneAsync(ref specularBitmapSource);
                        _ = SafeDispose.OneAsync(ref emissionBitmapSource);
                    }
                }

                if (texDict.TryGetValue(TextureUsage.SamplerMask, out var maskBitmapSourceAndIndex) &&
                    texDict.TryGetValue(TextureUsage.SamplerSpecular, out var specularBitmapSourceAndIndex)) {
                    var maskBitmapSource = maskBitmapSourceAndIndex.Item1;
                    var specularBitmapSource = specularBitmapSourceAndIndex.Item1;

                    WicBitmapSource? occlusionBitmapSource = null;
                    try {
                        occlusionBitmapSource = new(
                            maskBitmapSource.Width,
                            maskBitmapSource.Height,
                            maskBitmapSource.PixelFormat);
                        using (var maskBitmap = maskBitmapSource.AsBitmap())
                        using (var specularBitmap = specularBitmapSource.AsBitmap())
                        using (var occlusionBitmap = occlusionBitmapSource.AsBitmap())
                        using (var maskLock = maskBitmap.Lock(WICBitmapLockFlags.WICBitmapLockRead))
                        using (var specularLock = specularBitmap.Lock(
                                   WICBitmapLockFlags.WICBitmapLockRead | WICBitmapLockFlags.WICBitmapLockWrite))
                        using (var occlusionLock = occlusionBitmap.Lock(WICBitmapLockFlags.WICBitmapLockWrite)) {
                            unsafe void Blend() {
                                maskLock.Object.GetDataPointer(out var bufferSize, out var pbData).ThrowOnError();
                                var mask = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                specularLock.Object.GetDataPointer(out bufferSize, out pbData).ThrowOnError();
                                var specular = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                occlusionLock.Object.GetDataPointer(out bufferSize, out pbData).ThrowOnError();
                                var occlusion = new Span<ColorSetBlender.Bgra8888>(
                                    (ColorSetBlender.Bgra8888*) pbData,
                                    (int) bufferSize / 4);

                                for (var i = 0; i < mask.Length; i++) {
                                    var maskPixel = mask[i];
                                    var specularPixel = specular[i];

                                    specular[i].r = (byte) (specularPixel.r * Math.Pow(maskPixel.g / 255f, 2));
                                    specular[i].g = (byte) (specularPixel.g * Math.Pow(maskPixel.g / 255f, 2));
                                    specular[i].b = (byte) (specularPixel.b * Math.Pow(maskPixel.g / 255f, 2));
                                    occlusion[i] = new(maskPixel.r, maskPixel.r, maskPixel.r, 255);
                                }
                            }

                            Blend();
                        }

                        if (texDict.TryAdd(
                                TextureUsage.SamplerWaveMap,
                                Tuple.Create(occlusionBitmapSource, (int?) null)))
                            occlusionBitmapSource = null;
                    } finally {
                        _ = SafeDispose.OneAsync(ref occlusionBitmapSource);
                    }
                }
            }

            foreach (var (usage, (wic, textureIndexNullable)) in texDict) {
                var textureIndex = textureIndexNullable ?? AttachTexture(
                    $"{Path.GetFileNameWithoutExtension(material.Name)}_{usage}.png",
                    wic);
                switch (usage) {
                    case TextureUsage.SamplerDiffuse:
                    case TextureUsage.SamplerColorMap0:
                    case TextureUsage.SamplerColorMap1:
                        (material.PbrMetallicRoughness ??= new()).BaseColorTexture ??= new() {
                            Index = textureIndex,
                        };
                        break;
                    case TextureUsage.SamplerNormal:
                    case TextureUsage.SamplerNormalMap0:
                    case TextureUsage.SamplerNormalMap1:
                        material.NormalTexture ??= new() {
                            Index = textureIndex,
                        };
                        break;
                    case TextureUsage.SamplerSpecular:
                    case TextureUsage.SamplerSpecularMap0:
                    case TextureUsage.SamplerSpecularMap1:
                        Root.ExtensionsUsed.Add("KHR_materials_specular");
                        ((material.Extensions ??= new()).KhrMaterialsSpecular ??= new()).SpecularColorTexture ??= new() {
                            Index = textureIndex,
                        };
                        break;
                    case TextureUsage.SamplerWaveMap:
                        material.OcclusionTexture ??= new() {
                            Index = textureIndex,
                        };
                        break;
                    case TextureUsage.SamplerReflection:
                        material.EmissiveTexture ??= new() {
                            Index = textureIndex,
                        };
                        break;
                }
            }
        } finally {
            foreach (var (wbs, _) in texDict.Values)
                wbs.Dispose();
        }

        return Root.Materials.AddAndGetIndex(material);
    }

    public unsafe int AttachMesh(Model xivModel, int? skinIndex = null) {
        var mdlFile = xivModel.File!;
        var indexBufferView = AddBufferView(
            null,
            GltfBufferViewTarget.ElementArrayBuffer,
            new ReadOnlySpan<byte>(
                mdlFile.Data,
                (int) mdlFile.FileHeader.IndexOffset[(int) xivModel.Lod],
                (int) mdlFile.FileHeader.IndexBufferSize[(int) xivModel.Lod]));

        var boneNameToIndex = skinIndex is null
            ? null
            : Root
                .Skins[skinIndex.Value]
                .Joints
                .Select((x, i) => Tuple.Create(i, Root.Nodes[x].Name))
                .Where(x => x.Item2 is not null)
                .ToDictionary(x => x.Item2!, x => x.Item1);

        var mesh = new GltfMesh();
        foreach (var xivMesh in xivModel.Meshes) {
            var attributes = new GltfMeshPrimitiveAttributes();

            if (xivMesh.Vertices[0].Position is not null)
                attributes.Position = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.Position!.Value.NormalizePosition()).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

            if (xivMesh.Vertices[0].Normal is not null)
                attributes.Normal = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.Normal!.Value.NormalizeNormal()).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

            if (xivMesh.Vertices[0].Tangent1 is not null)
                attributes.Tangent = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.Tangent1!.Value.NormalizeTangent()).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

            if (xivMesh.Vertices[0].Color is not null)
                attributes.Color0 = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.Color!.Value).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

            if (xivMesh.Vertices[0].UV is not null)
                attributes.TexCoord0 = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.UV!.Value.NormalizeUv()).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

            if (xivMesh.Vertices[0].BlendWeights is not null && boneNameToIndex is not null) {
                attributes.Weights0 = AddAccessor(
                    null,
                    xivMesh.Vertices.Select(x => x.BlendWeights!.Value).ToArray().AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);

                var boneNames = xivMesh.BoneTable
                    .Select(b => xivMesh.Parent.StringOffsetToStringMap[(int) xivMesh.Parent.File!.BoneNameOffsets[b]])
                    .ToArray();

                var indices = xivMesh.Vertices
                    .Select(x => new TypedVec4<ushort>(
                        (ushort) (x.BlendWeights!.Value.X == 0 ? 0 : boneNameToIndex[boneNames[x.BlendIndices[0]]]),
                        (ushort) (x.BlendWeights!.Value.Y == 0 ? 0 : boneNameToIndex[boneNames[x.BlendIndices[1]]]),
                        (ushort) (x.BlendWeights!.Value.Z == 0 ? 0 : boneNameToIndex[boneNames[x.BlendIndices[2]]]),
                        (ushort) (x.BlendWeights!.Value.W == 0 ? 0 : boneNameToIndex[boneNames[x.BlendIndices[3]]])))
                    .ToArray();
                attributes.Joints0 = AddAccessor(
                    null,
                    indices.AsSpan(),
                    target: GltfBufferViewTarget.ArrayBuffer);
            }

            var xivMeshIndex = xivMesh.MeshIndex;
            var xivMaterialIndex = mdlFile.Meshes[xivMeshIndex].MaterialIndex;
            var materialIndex = FindMaterial(xivModel.Materials[xivMaterialIndex].MaterialPath);
            fixed (byte* pData = mdlFile.Data) {
                var indexSpan = new ReadOnlySpan<ushort>(
                    (ushort*) (pData + mdlFile.FileHeader.IndexOffset[(int) xivModel.Lod]),
                    (int) mdlFile.FileHeader.IndexBufferSize[(int) xivModel.Lod] / 2);

                if (xivMesh.Submeshes.Any()) {
                    foreach (var submesh in xivMesh.Submeshes) {
                        mesh.Primitives.Add(new() {
                            Attributes = attributes,
                            Indices = AddAccessor(
                                null,
                                indexSpan,
                                (int) submesh.IndexOffset,
                                (int) submesh.IndexNum,
                                indexBufferView,
                                GltfBufferViewTarget.ElementArrayBuffer),
                            Material = materialIndex,
                        });
                    }
                } else {
                    mesh.Primitives.Add(new() {
                        Attributes = attributes,
                        Indices = AddAccessor(
                            null,
                            indexSpan,
                            (int) mdlFile.Meshes[xivMesh.MeshIndex].StartIndex,
                            (int) mdlFile.Meshes[xivMesh.MeshIndex].IndexCount,
                            indexBufferView,
                            GltfBufferViewTarget.ElementArrayBuffer),
                        Material = materialIndex,
                    });
                }
            }
        }

        return Root.Meshes.AddAndGetIndex(mesh);
    }

    public int AttachAnimation(string name, IAnimation animation, int skinIndex) {
        var target = new GltfAnimation {
            Name = $"{name}",
        };

        void AddAnimationComponent<T>(
            GltfAnimationChannelTargetPath purpose,
            int boneNodeIndex,
            IEnumerable<T> values,
            IEnumerable<float> times) where T : unmanaged {
            var valueArray = values.ToArray();
            if (!valueArray.Any())
                return;

            var timesArray = times.ToArray();
            target.Channels.Add(new() {
                Sampler = target.Samplers.AddAndGetIndex(new() {
                    Input = AddAccessor(null, timesArray.AsSpan()),
                    Output = AddAccessor(null, valueArray.AsSpan()),
                    Interpolation = GltfAnimationSamplerInterpolation.Linear,
                }),
                Target = new() {
                    Node = boneNodeIndex,
                    Path = purpose,
                },
            });
        }

        foreach (var bone in animation.AffectedBoneIndices) {
            var node = Root.Skins[skinIndex].Joints[bone];

            var translation = animation.Translation(bone);
            if (!translation.IsEmpty) {
                var times = translation.GetFrameTimes().Append(animation.Duration).ToArray();
                var values = times.Select(x => translation.Interpolate(x)).ToArray();

                AddAnimationComponent(GltfAnimationChannelTargetPath.Translation, node, values, times);
            }

            var rotation = animation.Rotation(bone);
            if (!rotation.IsEmpty) {
                var times = rotation.GetFrameTimes().Append(animation.Duration).ToArray();
                var values = times.Select(x => rotation.Interpolate(x)).ToArray();

                AddAnimationComponent(GltfAnimationChannelTargetPath.Rotation, node, values, times);
            }

            var scale = animation.Scale(bone);
            if (!scale.IsEmpty) {
                var times = scale.GetFrameTimes().Append(animation.Duration).ToArray();
                var values = times.Select(x => scale.Interpolate(x)).ToArray();

                AddAnimationComponent(GltfAnimationChannelTargetPath.Scale, node, values, times);
            }
        }

        if (!target.Channels.Any() || !target.Samplers.Any())
            return -1;

        return Root.Animations.AddAndGetIndex(target);
    }
}
