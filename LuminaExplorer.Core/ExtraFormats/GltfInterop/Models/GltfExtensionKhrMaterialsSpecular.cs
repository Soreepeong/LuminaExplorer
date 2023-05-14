using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfExtensionKhrMaterialsSpecular : BaseGltfObject {
    [JsonProperty("specularFactor", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public float SpecularFactor = 1f;

    [JsonProperty("specularTexture", NullValueHandling = NullValueHandling.Ignore)]
    public GltfTextureInfo? SpecularTexture;

    [JsonProperty("specularColorFactor", DefaultValueHandling = DefaultValueHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore)]
    public float[]? SpecularColorFactor = {1f, 1f, 1f};

    [JsonProperty("specularColorTexture", NullValueHandling = NullValueHandling.Ignore)]
    public GltfTextureInfo? SpecularColorTexture;
}
