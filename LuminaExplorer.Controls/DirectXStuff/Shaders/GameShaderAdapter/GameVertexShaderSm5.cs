using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Data.Parsing;
using Lumina.Models.Models;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GameVertexShaderSm5 : DirectXObject {
    private readonly Dictionary<MdlStructs.VertexDeclarationStruct, nint> _inputLayoutDict = new();
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11VertexShader* _pShader;

    public GameVertexShaderSm5(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext, IShaderEntry shaderEntry) {
        if (shaderEntry.InputNames.Length != shaderEntry.InputTables.Length)
            throw new InvalidOperationException();

        try {
            ShaderEntry = shaderEntry;
            _pDevice = pDevice;
            _pDevice->AddRef();
            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();

            fixed (ID3D11VertexShader** p2 = &_pShader)
            fixed (void* pBytecode = ShaderEntry.ByteCode)
                ThrowH(pDevice->CreateVertexShader(pBytecode, (nuint) ShaderEntry.ByteCode.Length, null, p2));
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    public ID3D11InputLayout* GetInputLayout(MdlStructs.VertexDeclarationStruct mv) {
        lock (_inputLayoutDict) {
            fixed (byte* pBytecode = ShaderEntry.ByteCode)
            fixed (byte* pszPosition = "POSITION"u8.ToArray())
            fixed (byte* pszNormal = "NORMAL"u8.ToArray())
            fixed (byte* pszTexCoord = "TEXCOORD"u8.ToArray())
            fixed (byte* pszBlendWeight = "BLENDWEIGHT"u8.ToArray())
            fixed (byte* pszBlendIndices = "BLENDINDICES"u8.ToArray())
            fixed (byte* pszColor = "COLOR"u8.ToArray())
            fixed (byte* pszTangent = "TANGENT"u8.ToArray()) {
                if (_inputLayoutDict.TryGetValue(mv, out var pInputLayoutUntyped))
                    return (ID3D11InputLayout*) pInputLayoutUntyped;

                Span<InputElementDesc> descriptors = stackalloc InputElementDesc[8];
                var i = 0;
                for (; i < mv.VertexElements.Length; i++) {
                    var ve = mv.VertexElements[i];
                    var usage = (Vertex.VertexUsage) ve.Usage;
                    var type = (Vertex.VertexType) ve.Type;
                    descriptors[i] = new(
                        semanticName: usage switch {
                            Vertex.VertexUsage.Position => pszPosition,
                            Vertex.VertexUsage.BlendWeights => pszBlendWeight,
                            Vertex.VertexUsage.BlendIndices => pszBlendIndices,
                            Vertex.VertexUsage.Normal => pszNormal,
                            Vertex.VertexUsage.UV => pszTexCoord,
                            Vertex.VertexUsage.Tangent2 => pszTangent,
                            Vertex.VertexUsage.Tangent1 => pszTangent,
                            Vertex.VertexUsage.Color => pszColor,
                            _ => throw new NotSupportedException(),
                        },
                        semanticIndex: usage == Vertex.VertexUsage.Tangent2 ? 1u : 0u,
                        format: type switch {
                            Vertex.VertexType.Single3 => Format.FormatR32G32B32Float,
                            Vertex.VertexType.Single4 => Format.FormatR32G32B32Float,
                            Vertex.VertexType.UInt => Format.FormatR32Uint,
                            Vertex.VertexType.ByteFloat4 => Format.FormatR8G8B8A8Unorm,
                            Vertex.VertexType.Half2 => Format.FormatR16G16Float,
                            Vertex.VertexType.Half4 => Format.FormatR16G16B16A16Float,
                            _ => throw new NotSupportedException(),
                        },
                        inputSlot: 0u,
                        alignedByteOffset: ve.Offset,
                        inputSlotClass: InputClassification.PerVertexData);
                }

                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Position))
                    descriptors[i++] = new(pszPosition, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendWeights))
                    descriptors[i++] = new(pszBlendWeight, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendIndices))
                    descriptors[i++] = new(pszBlendIndices, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Normal))
                    descriptors[i++] = new(pszNormal, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.UV))
                    descriptors[i++] = new(pszTexCoord, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent2))
                    descriptors[i++] = new(pszTangent, 1, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent1))
                    descriptors[i++] = new(pszTangent, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Color))
                    descriptors[i] = new(pszColor, 0, Format.FormatR32G32B32A32Float, 0, 0,
                        InputClassification.PerVertexData);

                ID3D11InputLayout* pInputLayout = null;
                fixed (InputElementDesc* pDesc = descriptors)
                    ThrowH(_pDevice->CreateInputLayout(
                        pDesc,
                        (uint) descriptors.Length,
                        pBytecode,
                        (nuint) ShaderEntry.ByteCode.Length,
                        &pInputLayout));

                _inputLayoutDict.Add(mv, (nint) pInputLayout);
                return pInputLayout;
            }
        }
    }

    ~GameVertexShaderSm5() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        foreach (var v in _inputLayoutDict.Values)
            ((ID3D11InputLayout*) v)->Release();
        _inputLayoutDict.Clear();

        SafeRelease(ref _pShader);
        SafeRelease(ref _pDevice);
        SafeRelease(ref _pDeviceContext);
    }

    private void DisposePrivate(bool disposing) {
        _ = disposing;
        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(true);
        base.Dispose(disposing);
    }
    
    public IShaderEntry ShaderEntry { get; }

    public ID3D11VertexShader* Shader => _pShader;
}