using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GameVertexShaderSm5 : DirectXObject {
    private readonly Dictionary<MdlStructs.VertexDeclarationStruct, nint> _inputLayoutDict = new();
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11VertexShader* _pShader;

    public GameVertexShaderSm5(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext, IShaderEntry shaderEntry)
    {
        if (shaderEntry.InputNames.Length != shaderEntry.InputTables.Length)
            throw new InvalidOperationException();

        try {
            this.ShaderEntry = shaderEntry;
            this._pDevice = pDevice;
            this._pDevice->AddRef();
            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();

            fixed (ID3D11VertexShader** p2 = &this._pShader)
            fixed (void* pBytecode = this.ShaderEntry.ByteCode)
                pDevice->CreateVertexShader(pBytecode, (nuint) this.ShaderEntry.ByteCode.Length, null, p2).Ensure();
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    public ID3D11InputLayout* GetInputLayout(MdlStructs.VertexDeclarationStruct mv)
    {
        lock (this._inputLayoutDict) {
            fixed (byte* pBytecode = this.ShaderEntry.ByteCode)
            fixed (byte* pszPosition = "POSITION"u8)
            fixed (byte* pszNormal = "NORMAL"u8)
            fixed (byte* pszTexCoord = "TEXCOORD"u8)
            fixed (byte* pszBlendWeight = "BLENDWEIGHT"u8)
            fixed (byte* pszBlendIndices = "BLENDINDICES"u8)
            fixed (byte* pszColor = "COLOR"u8)
            fixed (byte* pszTangent = "TANGENT"u8) {
                if (this._inputLayoutDict.TryGetValue(mv, out var pInputLayoutUntyped))
                    return (ID3D11InputLayout*) pInputLayoutUntyped;

                Span<D3D11_INPUT_ELEMENT_DESC> descriptors = stackalloc D3D11_INPUT_ELEMENT_DESC[8];
                var i = 0;
                for (; i < mv.VertexElements.Length; i++) {
                    var ve = mv.VertexElements[i];
                    var usage = (Vertex.VertexUsage) ve.Usage;
                    var type = (Vertex.VertexType) ve.Type;
                    descriptors[i] = new() {
                        SemanticName = usage switch {
                            Vertex.VertexUsage.Position => (sbyte*) pszPosition,
                            Vertex.VertexUsage.BlendWeights => (sbyte*) pszBlendWeight,
                            Vertex.VertexUsage.BlendIndices => (sbyte*) pszBlendIndices,
                            Vertex.VertexUsage.Normal => (sbyte*) pszNormal,
                            Vertex.VertexUsage.UV => (sbyte*) pszTexCoord,
                            Vertex.VertexUsage.Tangent2 => (sbyte*) pszTangent,
                            Vertex.VertexUsage.Tangent1 => (sbyte*) pszTangent,
                            Vertex.VertexUsage.Color => (sbyte*) pszColor,
                            _ => throw new NotSupportedException(),
                        },
                        SemanticIndex = usage == Vertex.VertexUsage.Tangent2 ? 1u : 0u,
                        Format = type switch {
                            Vertex.VertexType.Single3 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                            Vertex.VertexType.Single4 => DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                            Vertex.VertexType.UInt => DXGI_FORMAT.DXGI_FORMAT_R32_UINT,
                            Vertex.VertexType.ByteFloat4 => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
                            Vertex.VertexType.Half2 => DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT,
                            Vertex.VertexType.Half4 => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
                            _ => throw new NotSupportedException(),
                        },
                        InputSlot = 0u,
                        AlignedByteOffset = ve.Offset,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                }

                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Position))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszPosition,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendWeights))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszBlendWeight,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendIndices))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszBlendIndices,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Normal))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszNormal,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.UV))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszTexCoord,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent2))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszTangent,
                        SemanticIndex = 1,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent1))
                    descriptors[i++] = new() {
                        SemanticName = (sbyte*) pszTangent,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Color))
                    descriptors[i] = new() {
                        SemanticName = (sbyte*) pszColor,
                        SemanticIndex = 0,
                        Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT,
                        InputSlot = 0,
                        AlignedByteOffset = 0,
                        InputSlotClass = D3D11_INPUT_CLASSIFICATION.D3D11_INPUT_PER_VERTEX_DATA,
                    };

                ID3D11InputLayout* pInputLayout = null;
                fixed (D3D11_INPUT_ELEMENT_DESC* pDesc = descriptors)
                    this._pDevice->CreateInputLayout(
                        pDesc,
                        (uint) descriptors.Length,
                        pBytecode,
                        (nuint) this.ShaderEntry.ByteCode.Length,
                        &pInputLayout).Ensure();

                this._inputLayoutDict.Add(mv, (nint) pInputLayout);
                return pInputLayout;
            }
        }
    }

    ~GameVertexShaderSm5() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        foreach (var v in this._inputLayoutDict.Values)
            ((ID3D11InputLayout*) v)->Release();
        this._inputLayoutDict.Clear();

        SafeRelease(ref this._pShader);
        SafeRelease(ref this._pDevice);
        SafeRelease(ref this._pDeviceContext);
    }

    private void DisposePrivate(bool disposing)
    {
        _ = disposing;
        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(true);
        base.Dispose(disposing);
    }

    public IShaderEntry ShaderEntry { get; }

    public ID3D11VertexShader* Shader => this._pShader;
}
