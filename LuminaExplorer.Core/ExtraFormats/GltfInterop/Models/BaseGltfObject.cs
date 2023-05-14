using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class BaseGltfObject {
    [JsonProperty("extensions", NullValueHandling = NullValueHandling.Ignore)]
    public GltfExtensions? Extensions;
}
