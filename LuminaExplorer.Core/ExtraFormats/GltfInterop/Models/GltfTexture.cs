using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfTexture : BaseGltfObject {
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name;

    [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)]
    public int? Source;
}
