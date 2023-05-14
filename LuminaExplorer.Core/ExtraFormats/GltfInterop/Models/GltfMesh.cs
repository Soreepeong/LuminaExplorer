using System.Collections.Generic;
using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfMesh : BaseGltfObject {
    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name;

    [JsonProperty("primitives")] public List<GltfMeshPrimitive> Primitives = new();
}
