using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfExtensionMsftTextureDds : BaseGltfObject {
    [JsonProperty("source")] public int Source;
}
