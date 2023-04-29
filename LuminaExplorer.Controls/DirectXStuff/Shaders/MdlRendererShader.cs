using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Lumina.Data.Parsing;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public unsafe class MdlRendererShader : DirectXObject {
    private readonly byte[] _bytecodeVs;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11PixelShader* _pPixelShader;
    private ID3D11VertexShader* _pVertexShader;
    private ID3D11InputLayout* _pInputLayout;
    private readonly ID3D11SamplerState*[] _pSamplers;

    private readonly Dictionary<MdlStructs.VertexDeclarationStruct, nint> _inputLayoutDict = new();

    public MdlRendererShader(ID3D11Device* pDevice, ID3D11DeviceContext* pContext) {
        try {
            _pDevice = pDevice;
            _pDevice->AddRef();

            _pDeviceContext = pContext;
            _pDeviceContext->AddRef();

            var samplerDesc = new SamplerDesc {
                Filter = Filter.MinMagMipLinear,
                MaxAnisotropy = 0,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MipLODBias = 0f,
                MinLOD = 0,
                MaxLOD = float.MaxValue,
                ComparisonFunc = ComparisonFunc.Never,
            };
            fixed (ID3D11SamplerState** ppSamplers = _pSamplers = new ID3D11SamplerState*[4]) {
                ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + 0));
                ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + 1));
                ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + 2));
                ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + 3));
                ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + 4));
            }

            var bytecode = GetType().CompileShaderFromAssemblyResource("ps_4_0", "main_ps");
            fixed (ID3D11PixelShader** p2 = &_pPixelShader)
            fixed (void* pBytecode = bytecode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2));

            _bytecodeVs = bytecode = GetType().CompileShaderFromAssemblyResource("vs_4_0", "main_vs");
            fixed (ID3D11VertexShader** ppVertexShader = &_pVertexShader)
            fixed (ID3D11InputLayout** ppInputLayout = &_pInputLayout)
            fixed (byte* pBytecode = bytecode)
            fixed (byte* pszPosition = "POSITION"u8.ToArray())
            fixed (byte* pszNormal = "NORMAL"u8.ToArray())
            fixed (byte* pszTexCoord = "TEXCOORD"u8.ToArray())
            fixed (byte* pszBlendWeight = "BLENDWEIGHT"u8.ToArray())
            fixed (byte* pszBlendIndices = "BLENDINDICES"u8.ToArray())
            fixed (byte* pszColor = "COLOR"u8.ToArray())
            fixed (byte* pszTangent = "TANGENT"u8.ToArray()) {
                ThrowH(pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, ppVertexShader));

                Span<InputElementDesc> desc = stackalloc InputElementDesc[] {
                    new(pszPosition, 0, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                    new(pszBlendWeight, 0, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                    new(pszBlendIndices, 0, Format.FormatR8G8B8A8Uint, 0, D3D11.AppendAlignedElement),
                    new(pszNormal, 0, Format.FormatR32G32B32Float, 0, D3D11.AppendAlignedElement),
                    new(pszTexCoord, 0, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                    new(pszTangent, 1, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                    new(pszTangent, 0, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                    new(pszColor, 0, Format.FormatR32G32B32A32Float, 0, D3D11.AppendAlignedElement),
                };

                fixed (InputElementDesc* pDesc = desc)
                    ThrowH(_pDevice->CreateInputLayout(
                        pDesc,
                        (uint) desc.Length,
                        pBytecode,
                        (nuint) bytecode.Length,
                        ppInputLayout));
            }
        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing) {
        foreach (var v in _inputLayoutDict.Values)
            ((ID3D11InputLayout*) v)->Release();
        _inputLayoutDict.Clear();
        for (var i = 0; i < _pSamplers.Length; i++)
            SafeRelease(ref _pSamplers[i]);
        SafeRelease(ref _pPixelShader);
        SafeRelease(ref _pVertexShader);
        SafeRelease(ref _pInputLayout);
        SafeRelease(ref _pDevice);
        SafeRelease(ref _pDeviceContext);
        base.Dispose(disposing);
    }

    public void Draw(ConstantBufferResource<CameraParameters> cameraParameters, ModelObject modelObject) {
        _pDeviceContext->IASetInputLayout(_pInputLayout);
        _pDeviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);

        _pDeviceContext->VSSetShader(_pVertexShader, null, 0);
        _pDeviceContext->VSSetConstantBuffers(0, 1, cameraParameters.Buffer);

        _pDeviceContext->PSSetShader(_pPixelShader, null, 0);
        _pDeviceContext->PSSetConstantBuffers(0, 1, cameraParameters.Buffer);
        fixed (ID3D11SamplerState** ppSamplers = _pSamplers)
            _pDeviceContext->PSSetSamplers(0, (uint) _pSamplers.Length, ppSamplers);

        for (var i = 0; modelObject.GetBuffers(i, out var pVertexBuffer, out var pIndexBuffer); i++) {
            var submeshes = modelObject.GetSubmeshes(i);
            _pDeviceContext->IASetVertexBuffers(0, 1, pVertexBuffer, (uint) Unsafe.SizeOf<VsInput>(), 0);
            _pDeviceContext->IASetIndexBuffer(pIndexBuffer, Format.FormatR16Uint, 0);

            if (modelObject.TryGetMaterial(i, out var materialIndex, out var material)) {
                for (var j = 0; j < material.Textures.Length; j++) {
                    var t = material.Textures[j];
                    var pTexture = modelObject.GetTexture(materialIndex, j);
                    switch (t.TextureUsageSimple) {
                        case Texture.Usage.Diffuse:
                            _pDeviceContext->PSSetShaderResources(0, 1, pTexture);
                            break;
                        case Texture.Usage.Normal:
                            _pDeviceContext->PSSetShaderResources(1, 1, pTexture);
                            break;
                        case Texture.Usage.Specular:
                            _pDeviceContext->PSSetShaderResources(2, 1, pTexture);
                            break;
                        case Texture.Usage.Mask:
                            _pDeviceContext->PSSetShaderResources(3, 1, pTexture);
                            break;
                    }
                }
            }

            if (submeshes.Any()) {
                foreach (var submesh in submeshes)
                    _pDeviceContext->DrawIndexed(submesh.IndexNum, submesh.IndexOffset, 0);
            } else {
                _pDeviceContext->DrawIndexed((uint) modelObject.GetNumIndices(i), 0, 0);
            }
        }
    }

    public ID3D11InputLayout* GetInputLayout(MdlStructs.VertexDeclarationStruct mv) {
        lock (_inputLayoutDict) {
            fixed (byte* pBytecode = _bytecodeVs)
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
                    descriptors[i++] = new(pszPosition, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendWeights))
                    descriptors[i++] = new(pszBlendWeight, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.BlendIndices))
                    descriptors[i++] = new(pszBlendIndices, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Normal))
                    descriptors[i++] = new(pszNormal, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.UV))
                    descriptors[i++] = new(pszTexCoord, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent2))
                    descriptors[i++] = new(pszTangent, 1, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Tangent1))
                    descriptors[i++] = new(pszTangent, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);
                if (mv.VertexElements.All(x => x.Usage != (uint) Vertex.VertexUsage.Color))
                    descriptors[i] = new(pszColor, 0, Format.FormatR32G32B32A32Float, 0, 0, InputClassification.PerVertexData);

                ID3D11InputLayout* pInputLayout = null;
                fixed (InputElementDesc* pDesc = descriptors)
                    ThrowH(_pDevice->CreateInputLayout(
                        pDesc,
                        (uint) descriptors.Length,
                        pBytecode,
                        (nuint) _bytecodeVs.Length,
                        &pInputLayout));

                _inputLayoutDict.Add(mv, (nint) pInputLayout);
                return pInputLayout;
            }
        }
    }

    public class ModelObject : DirectXObject {
        private readonly Model _model;
        private readonly Mesh[] _meshes;
        private readonly ID3D11Buffer*[] _meshVertices;
        private readonly ID3D11Buffer*[] _meshIndices;
        private readonly Task<Texture2DShaderResource?>[][] _textures;

        private Texture2DShaderResource _dummy;

        public ModelObject(MdlRendererShader shader, Model model, Func<string, Task<DdsFile?>> ddsCallback) {
            var dummyData = new Matrix4x4();

            try {
                _dummy = new(shader._pDevice, Format.FormatR8G8B8A8Unorm, 4, 4, 16, (nint) (&dummyData));
                _model = model;
                _textures = new Task<Texture2DShaderResource?>[model.Materials.Length][];

                for (var i = 0; i < model.Materials.Length; i++) {
                    var mat = (Material?) model.Materials[i];
                    _textures[i] = new Task<Texture2DShaderResource?>[mat?.Textures.Length ?? 0];
                    if (mat is null)
                        continue;

                    for (var j = 0; j < mat.Textures.Length; j++) {
                        var m = mat.Textures[j];
                        if (m.TexturePath == "dummy.tex") {
                            _textures[i][j] = Task.FromResult((Texture2DShaderResource?) null);
                            continue;
                        }

                        var pDevice = shader._pDevice;
                        pDevice->AddRef();
                        _textures[i][j] = ddsCallback(m.TexturePath).ContinueWith(r => {
                            try {
                                if (!r.IsCompletedSuccessfully || r.Result is null)
                                    return null;
                                return new Texture2DShaderResource(pDevice, r.Result);
                            } finally {
                                pDevice->Release();
                            }
                        });

                        _textures[i][j].ContinueWith(_ => TextureLoadStateChanged?.Invoke());
                    }
                }

                _meshes = model.Meshes
                    .Where(x => x.Types.Contains(Mesh.MeshType.Main))
                    .Where(x => x.Vertices.Any())
                    .ToArray();
                _meshVertices = new ID3D11Buffer*[_meshes.Length];
                _meshIndices = new ID3D11Buffer*[_meshes.Length];
                for (var i = 0; i < _meshes.Length; i++) {
                    var mesh = _meshes[i];
                    var vertices = new VsInput[mesh.Vertices.Length];
                    for (var j = 0; j < mesh.Vertices.Length; j++) {
                        var source = mesh.Vertices[j];
                        vertices[j] = new() {
                            Position = (source.Position ?? Vector4.Zero) with {W = 1f},
                            BlendWeight = source.BlendWeights ?? Vector4.Zero,
                            BlendIndices = new(
                                source.BlendIndices[0],
                                source.BlendIndices[1],
                                source.BlendIndices[2],
                                source.BlendIndices[3]),
                            Normal = source.Normal ?? Vector3.Zero,
                            Uv = source.UV ?? Vector4.Zero,
                            Tangent2 = source.Tangent2 ?? Vector4.Zero,
                            Tangent1 = source.Tangent1 ?? Vector4.Zero,
                            Color = source.Color ?? Vector4.Zero,
                        };
                    }

                    fixed (void* pVertices = vertices)
                    fixed (ID3D11Buffer** ppVertexBuffer = &_meshVertices[i]) {
                        var vertexData = new SubresourceData(pVertices);
                        var vertexBufferDesc = new BufferDesc(
                            (uint) (Unsafe.SizeOf<VsInput>() * vertices.Length),
                            Usage.Default,
                            (uint) BindFlag.VertexBuffer);
                        ThrowH(shader._pDevice->CreateBuffer(&vertexBufferDesc, &vertexData, ppVertexBuffer));
                    }

                    fixed (void* pIndices = mesh.Indices)
                    fixed (ID3D11Buffer** ppIndexBuffer = &_meshIndices[i]) {
                        var indexData = new SubresourceData(pIndices);
                        var indexBufferDesc = new BufferDesc(
                            (uint) Buffer.ByteLength(mesh.Indices),
                            Usage.Default,
                            (uint) BindFlag.IndexBuffer);
                        ThrowH(shader._pDevice->CreateBuffer(&indexBufferDesc, &indexData, ppIndexBuffer));
                    }
                }
            } catch (Exception) {
                DisposeInner(true);
                throw;
            }
        }

        public event Action? TextureLoadStateChanged;

        public bool GetBuffers(int i, out ID3D11Buffer* pVertexBuffer, out ID3D11Buffer* pIndexBuffer) {
            pVertexBuffer = pIndexBuffer = null;
            if (i >= _meshVertices.Length || i < 0)
                return false;

            pVertexBuffer = _meshVertices[i];
            pIndexBuffer = _meshIndices[i];
            return true;
        }

        public bool TryGetMaterial(int meshIndex, out int materialIndex, [MaybeNullWhen(false)] out Material material) {
            materialIndex = _model.File!.Meshes[_meshes[meshIndex].MeshIndex].MaterialIndex;
            material = _model.Materials[materialIndex];
            return true;
        }

        public ID3D11ShaderResourceView* GetTexture(int materialIndex, int modelIndex) =>
            (_textures[materialIndex][modelIndex] is { IsCompletedSuccessfully: true, Result: { } r }
                ? r
                : _dummy).ShaderResourceView;

        public int GetNumIndices(int i) => _meshes[i].Indices.Length;

        public Submesh[] GetSubmeshes(int i) => _meshes[i].Submeshes;

        private void DisposeInner(bool disposing) {
            if (disposing) {
                SafeDispose.One(ref _dummy!);
                for (var i = 0; i < _textures.Length; i++)
                    _ = SafeDispose.EnumerableAsync(ref _textures[i]);
            }

            for (var i = 0; i < _meshVertices.Length; i++)
                SafeRelease(ref _meshVertices[i]);
            for (var i = 0; i < _meshIndices.Length; i++)
                SafeRelease(ref _meshIndices[i]);
        }

        protected override void Dispose(bool disposing) {
            DisposeInner(disposing);
            base.Dispose(disposing);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CameraParameters {
        public Matrix4X4<float> ViewProjection;

        public CameraParameters() { }

        public CameraParameters(Matrix4x4 projection, Matrix4x4 view) {
            ViewProjection = Matrix4x4.Multiply(view, projection).ToSilkValue();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VsInput {
        public Vector4 Position;
        public Vector4 BlendWeight;
        public Vector4D<byte> BlendIndices;
        public Vector3 Normal;
        public Vector4 Uv;
        public Vector4 Tangent2;
        public Vector4 Tangent1;
        public Vector4 Color;
    }
}
