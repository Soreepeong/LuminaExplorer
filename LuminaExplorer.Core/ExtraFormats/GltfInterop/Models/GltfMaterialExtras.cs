using System.Collections.Generic;
using Lumina.Data.Parsing;
using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfMaterialExtras : BaseGltfObject {
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string? ShaderPack;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int VariantId;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<ColorSetWithString>? UvColorSets;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<ColorSetWithString>? ColorSets;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public ushort[]? ColorSetInfo;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public ushort[]? ColorSetDyeInfo;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<ShaderKey>? ShaderKeys;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<Constant>? Constants;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<SafeSampler>? Samplers;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public List<float>? ShaderValues;

    public struct ColorSetWithString {
        public string Name;
        public byte Index;
        public byte Unknown1;
    }

    public struct SafeSampler {
        public TextureUsage TextureUsage;
        public uint Flags;
        public string TexturePath;
        public int? TextureIndex;
    }
}
