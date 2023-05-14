using Newtonsoft.Json;

namespace LuminaExplorer.Core.ExtraFormats.GltfInterop.Models;

public class GltfExtensions : BaseGltfObject {
    [JsonProperty("KHR_materials_specular", NullValueHandling = NullValueHandling.Ignore)]
    public GltfExtensionKhrMaterialsSpecular? KhrMaterialsSpecular;

    [JsonProperty("MSFT_texture_dds", NullValueHandling = NullValueHandling.Ignore)]
    public GltfExtensionMsftTextureDds? MsftTextureDds;
}
