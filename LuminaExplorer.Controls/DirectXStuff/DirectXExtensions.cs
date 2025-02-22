using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff;

public static class DirectXExtensions {
    public static unsafe void SetVertex(
        ref this D3D11_INPUT_ELEMENT_DESC desc,
        byte* semanticName,
        uint semanticIndex,
        DXGI_FORMAT format,
        uint inputSlot = 0,
        uint alignedByteOffset = D3D11.D3D11_APPEND_ALIGNED_ELEMENT,
        uint instanceDataStepRate = 0)
    {
        desc.SemanticName = (sbyte*) semanticName;
        desc.SemanticIndex = semanticIndex;
        desc.Format = format;
        desc.InputSlot = inputSlot;
        desc.AlignedByteOffset = alignedByteOffset;
        desc.InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA;
        desc.InstanceDataStepRate = instanceDataStepRate;
    }
}
