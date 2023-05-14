using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Lumina.Data.Files;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;
using LuminaExplorer.Core.Util;
using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop;

public class GltfTuple {
    private readonly GltfRoot _root;
    private readonly MemoryStream _data;

    public GltfTuple() {
        _root = new();
        _data = new();
        _root.Buffers.Add(new());
        _root.Scene = _root.Scenes.AddAndGetIndex(new());
    }

    public void Compile(Stream target) {
        _root.Buffers[0].ByteLength = _data.Length;

        var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_root));
        if (json.Length % 4 != 0) {
            var rv = new byte[(json.Length + 3) / 4 * 4];
            Buffer.BlockCopy(json, 0, rv, 0, json.Length);
            for (var i = json.Length; i < rv.Length; i++)
                rv[i] = 0x20; // space
            json = rv;
        }

        using var writer = new NativeWriter(target, Encoding.UTF8, true) {IsBigEndian = false};
        writer.Write(0x46546C67);
        writer.Write(2);
        writer.Write(checked(12 + 8 + json.Length + 8 + (int) _data.Length));
        writer.Write(json.Length);
        writer.Write(0x4E4F534A);
        writer.Write(json);

        writer.Write(checked((int) _data.Length));
        writer.Write(0x004E4942);
        _data.Position = 0;
        _data.CopyTo(target);
    }

    public unsafe int AddBufferView<T>(
        string? baseName,
        GltfBufferViewTarget? target,
        ReadOnlySpan<T> data)
        where T : unmanaged {
        var byteLength = sizeof(T) * data.Length;
        var index = _root.BufferViews.AddAndGetIndex(new() {
            Name = baseName is null ? null : $"{baseName}/bufferView",
            ByteOffset = checked((int) (_data.Position = (_data.Length + 3) / 4 * 4)),
            ByteLength = byteLength,
            Target = target,
        });

        fixed (void* src = data)
            _data.Write(new((byte*) src, byteLength));

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

            return _root.Accessors.AddAndGetIndex(accessor);
        }
    }

    public void AddToScene(int meshIndex, int? skinIndex) =>
        _root.Scenes[_root.Scene].Nodes.Add(_root.Nodes.AddAndGetIndex(new() {
            Skin = skinIndex,
            Mesh = meshIndex,
            Children = skinIndex is null ? new() : new() {_root.Skins[skinIndex.Value].Joints[0]},
        }));

    public int? FindMaterial(string mtrlFilePath) {
        var name = Path.GetFileNameWithoutExtension(mtrlFilePath);
        for (var i = 0; i < _root.Materials.Count; i++)
            if (_root.Materials[i].Name == name)
                return i;
        return null;
    }

    public int AttachSkin(SklbFile sklbFile) {
        var firstGltfNodeIndex = _root.Nodes.AddRangeAndGetIndex(sklbFile.Bones.Select(bone => new GltfNode {
            Name = bone.Name,
            Children = bone.Children.Select(x => x.Index).ToList(),
            Translation = bone.Translation.ToFloatList(Vector3.Zero, 1e-6f),
            Rotation = Quaternion.Normalize(bone.Rotation).ToFloatList(Quaternion.Identity, 1e-6f),
            Scale = bone.Scale.ToFloatList(Vector3.One, 1e-6f),
        }).ToArray());

        return _root.Skins.AddAndGetIndex(new() {
            InverseBindMatrices = AddAccessor(
                null,
                sklbFile.Bones.Select(x => x.BindPoseAbsoluteInverse.Normalize())
                    .ToArray()
                    .AsSpan()),
            Joints = _root.Nodes.Select((_, i) => firstGltfNodeIndex + i).ToList(),
        });
    }

    public int AttachTexture(string name, DdsFile ddsFile) {
        for (var i = 0; i < _root.Textures.Count; i++)
            if (_root.Textures[i].Name == name)
                return i;

        using var png = new MemoryStream();
        using (var wicBitmapSource = ddsFile.ToWicBitmapSource(0, 0, 0))
            wicBitmapSource.Save(png, WicNet.WicCodec.GUID_ContainerFormatPng);

        _root.ExtensionsUsed.Add("MSFT_texture_dds");
        return _root.Textures.AddAndGetIndex(new() {
            Name = name,
            Source = _root.Images.AddAndGetIndex(new() {
                Name = Path.ChangeExtension(name, ".png"),
                MimeType = "image/png",
                BufferView = AddBufferView(
                    name + ".png",
                    null,
                    new ReadOnlySpan<byte>(png.GetBuffer(), 0, (int) png.Length)),
            }),
            Extensions = new() {
                MsftTextureDds = new() {
                    Source = _root.Images.AddAndGetIndex(new() {
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

        for (var i = 0; i < _root.Materials.Count; i++)
            if (_root.Materials[i].Name == name)
                return i;

        var material = new GltfMaterial {
            Name = name,
            Extras = new() {
                ShaderPack = xivMaterial.ShaderPack,
            },
        };

        foreach (var t in xivMaterial.Textures) {
            if (t.TexturePath == "dummy.tex")
                continue;

            var texFile = await texFileGetter(t.TexturePath);
            if (texFile is null)
                continue;

            var textureIndex = AttachTexture(t.TexturePath, texFile.ToDdsFile());
            material.Extras.AssociatedTextures[t.TextureUsageRaw.ToString()] = textureIndex;
            switch (t.TextureUsageSimple) {
                case Texture.Usage.Diffuse:
                    (material.PbrMetallicRoughness ??= new()).BaseColorTexture ??= new() {
                        Index = textureIndex,
                    };
                    break;
                case Texture.Usage.Normal:
                    material.NormalTexture ??= new() {
                        Index = textureIndex,
                    };
                    break;
                case Texture.Usage.Specular:
                    _root.ExtensionsUsed.Add("KHR_materials_specular");
                    ((material.Extensions ??= new()).KhrMaterialsSpecular ??= new()).SpecularColorTexture ??= new() {
                        Index = textureIndex,
                    };
                    break;
                case Texture.Usage.Wave:
                    material.OcclusionTexture ??= new() {
                        Index = textureIndex,
                    };
                    break;
                case Texture.Usage.Reflection:
                    material.EmissiveTexture ??= new() {
                        Index = textureIndex,
                    };
                    break;
            }
        }

        return _root.Materials.AddAndGetIndex(material);
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
            : _root
                .Skins[skinIndex.Value]
                .Joints
                .Select((x, i) => Tuple.Create(i, _root.Nodes[x].Name))
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

        return _root.Meshes.AddAndGetIndex(mesh);
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
            var node = _root.Skins[skinIndex].Joints[bone];

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

        return _root.Animations.AddAndGetIndex(target);
    }
}
