using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfSampler : BaseGltfObject {
    [JsonProperty("magFilter")] public GltfSamplerFilters MagFilter = GltfSamplerFilters.Linear;

    [JsonProperty("minFilter")] public GltfSamplerFilters MinFilter = GltfSamplerFilters.LinearMipmapLinear;
}
