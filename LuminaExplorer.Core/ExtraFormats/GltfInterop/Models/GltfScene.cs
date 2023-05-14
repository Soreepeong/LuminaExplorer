using System.Collections.Generic;
using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfScene : BaseGltfObject {
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name;

    [JsonProperty("nodes")] public List<int> Nodes = new();
}
