using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfBuffer : BaseGltfObject {
    [JsonProperty("byteLength")] public long ByteLength;

    [JsonProperty("uri", NullValueHandling = NullValueHandling.Ignore)]
    public string? Uri;
}
