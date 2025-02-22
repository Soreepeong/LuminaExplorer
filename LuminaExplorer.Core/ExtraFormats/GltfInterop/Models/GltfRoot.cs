using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfRoot : BaseGltfObject {
    [JsonProperty("asset")] public GltfAsset Asset = new();

    [JsonProperty("extensionsUsed")] public HashSet<string> ExtensionsUsed = new();

    [JsonProperty("scene")] public int Scene;

    [JsonProperty("scenes")] public List<GltfScene> Scenes = new();

    [JsonProperty("nodes")] public List<GltfNode> Nodes = new();

    [JsonProperty("animations")] public List<GltfAnimation> Animations = new();

    [JsonProperty("materials")] public List<GltfMaterial> Materials = new();

    [JsonProperty("meshes")] public List<GltfMesh> Meshes = new();

    [JsonProperty("textures")] public List<GltfTexture> Textures = new();

    [JsonProperty("images")] public List<GltfImage> Images = new();

    [JsonProperty("skins")] public List<GltfSkin> Skins = new();

    [JsonProperty("accessors")] public List<GltfAccessor> Accessors = new();

    [JsonProperty("bufferViews")] public List<GltfBufferView> BufferViews = new();

    [JsonProperty("samplers")] public List<GltfSampler> Samplers = new();

    [JsonProperty("buffers")] public List<GltfBuffer> Buffers = new();

    [UsedImplicitly]
    public bool ShouldSerializeExtensionsUsed() => this.ExtensionsUsed.Any();

    [UsedImplicitly]
    public bool ShouldSerializeScenes() => this.Scenes.Any();

    [UsedImplicitly]
    public bool ShouldSerializeNodes() => this.Nodes.Any();

    [UsedImplicitly]
    public bool ShouldSerializeAnimations() => this.Animations.Any();

    [UsedImplicitly]
    public bool ShouldSerializeMaterials() => this.Materials.Any();

    [UsedImplicitly]
    public bool ShouldSerializeMeshes() => this.Meshes.Any();

    [UsedImplicitly]
    public bool ShouldSerializeTextures() => this.Textures.Any();

    [UsedImplicitly]
    public bool ShouldSerializeImages() => this.Images.Any();

    [UsedImplicitly]
    public bool ShouldSerializeSkins() => this.Skins.Any();

    [UsedImplicitly]
    public bool ShouldSerializeAccessors() => this.Accessors.Any();

    [UsedImplicitly]
    public bool ShouldSerializeBufferViews() => this.BufferViews.Any();

    [UsedImplicitly]
    public bool ShouldSerializeSamplers() => this.Samplers.Any();

    [UsedImplicitly]
    public bool ShouldSerializeBuffers() => this.Buffers.Any();
}
