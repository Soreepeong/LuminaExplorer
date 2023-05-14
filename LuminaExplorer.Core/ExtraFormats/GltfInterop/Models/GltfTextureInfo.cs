using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfTextureInfo : BaseGltfObject {
    [JsonProperty("index")] public int Index;

    [JsonProperty("texCoord", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int TexCoord;
}
