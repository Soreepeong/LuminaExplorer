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

    public void Draw(State state, ModelObject modelObject) {
        _pDeviceContext->IASetInputLayout(_pInputLayout);
        _pDeviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);

        _pDeviceContext->VSSetShader(_pVertexShader, null, 0);
        _pDeviceContext->PSSetShader(_pPixelShader, null, 0);
        fixed (ID3D11SamplerState** ppSamplers = _pSamplers)
            _pDeviceContext->PSSetSamplers(0, (uint) _pSamplers.Length, ppSamplers);

        state.BindConstantBuffers();
        
        _pDeviceContext->IASetIndexBuffer(modelObject.IndexBuffer, Format.FormatR16Uint, 0);

        for (var i = 0; modelObject.GetVertexBuffer(i, out var pVertexBuffer); i++) {
            var submeshes = modelObject.GetSubmeshes(i);
            _pDeviceContext->IASetVertexBuffers(0, 1, pVertexBuffer, (uint) Unsafe.SizeOf<VsInput>(), 0);

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
        private readonly Task<Texture2DShaderResource?>[][] _textures;
        private ID3D11Buffer* _pIndexBuffer;
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

                }

                fixed (void* pIndexData = &model.File!.Data[model.File!.FileHeader.IndexOffset[(int) model.Lod]])
                fixed (ID3D11Buffer** ppIndexBuffer = &_pIndexBuffer) {
                    var indexData = new SubresourceData(pIndexData);
                    var indexBufferDesc = new BufferDesc(
                        model.File.FileHeader.IndexBufferSize[(int) model.Lod],
                        Usage.Default,
                        (uint) BindFlag.IndexBuffer);
                    ThrowH(shader._pDevice->CreateBuffer(&indexBufferDesc, &indexData, ppIndexBuffer));
                }
            } catch (Exception) {
                DisposeInner(true);
                throw;
            }
        }

        public event Action? TextureLoadStateChanged;

        public ID3D11Buffer* IndexBuffer => _pIndexBuffer; 

        public bool GetVertexBuffer(int i, out ID3D11Buffer* pVertexBuffer) {
            pVertexBuffer = null;
            if (i >= _meshVertices.Length || i < 0)
                return false;

            pVertexBuffer = _meshVertices[i];
            return true;
        }

        public bool TryGetMaterial(int meshIndex, out int materialIndex, [MaybeNullWhen(false)] out Material material) {
            materialIndex = _model.File!.Meshes[_meshes[meshIndex].MeshIndex].MaterialIndex;
            material = _model.Materials[materialIndex];
            return true;
        }

        public ID3D11ShaderResourceView* GetTexture(int materialIndex, int modelIndex) =>
            (_textures[materialIndex][modelIndex] is {IsCompletedSuccessfully: true, Result: { } r}
                ? r
                : _dummy).ShaderResourceView;

        public int GetNumIndices(int i) => _meshes[i].Indices.Length;

        public Submesh[] GetSubmeshes(int i) => _meshes[i].Submeshes;

        private void DisposeInner(bool disposing) {
            if (disposing) {
                SafeDispose.One(ref _dummy!);
                for (var i = 0; i < _textures.Length; i++) {
                    foreach (var t1 in _textures[i])
                        t1.Dispose();

                    _textures[i] = Array.Empty<Task<Texture2DShaderResource?>>();
                }
            }

            for (var i = 0; i < _meshVertices.Length; i++)
                SafeRelease(ref _meshVertices[i]);
            SafeRelease(ref _pIndexBuffer);
        }

        protected override void Dispose(bool disposing) {
            DisposeInner(disposing);
            base.Dispose(disposing);
        }
    }

    public sealed class State : IDisposable {
        private ID3D11DeviceContext* _pContext;
        private ConstantBufferResource<CameraParameters> _cameraBufferResource;
        private ConstantBufferResource<LightParameters> _lightBufferResource;
        private readonly ID3D11Buffer*[] _buffers = new ID3D11Buffer*[2];

        private CameraParameters _camera = new();
        private LightParameters _light = new();

        public State(ID3D11Device* pDevice, ID3D11DeviceContext* pContext) {
            try {
                _pContext = pContext;
                _pContext->AddRef();
                _cameraBufferResource = new(pDevice, pContext);
                _lightBufferResource = new(pDevice, pContext);

                _buffers[0] = _cameraBufferResource.Buffer;
                _buffers[1] = _lightBufferResource.Buffer;
            } catch (Exception) {
                Dispose();
                throw;
            }
        }

        ~State() {
            Dispose(false);
        }

        private void Dispose(bool disposing) {
            if (disposing) {
                SafeDispose.One(ref _cameraBufferResource!);
                SafeDispose.One(ref _lightBufferResource!);
            }

            SafeRelease(ref _pContext);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public CameraParameters Camera {
            get => _camera;
            set {
                _camera = value;
                _cameraBufferResource.MarkUpdateRequired();
            }
        }

        public LightParameters Light {
            get => _light;
            set {
                _light = value;
                _lightBufferResource.MarkUpdateRequired();
            }
        }

        public void BindConstantBuffers() {
            fixed (ID3D11Buffer** ppBuffers = _buffers) {
                if (_cameraBufferResource.UpdateRequired)
                    _cameraBufferResource.UpdateData(_camera);
                if (_lightBufferResource.UpdateRequired)
                    _lightBufferResource.UpdateData(_light);

                _pContext->VSSetConstantBuffers(0, (uint) _buffers.Length, ppBuffers);
                _pContext->PSSetConstantBuffers(0, (uint) _buffers.Length, ppBuffers);
            }
        }

        public void UpdateCamera(Matrix4x4 world, Matrix4x4 view, Matrix4x4 projection) {
            _camera.View = view;
            _camera.InverseView = Matrix4x4.Invert(_camera.View, out var inverse)
                ? inverse
                : Matrix4x4.Identity;
            _camera.ViewProjection = Matrix4x4.Multiply(view, projection);
            _camera.InverseViewProjection = Matrix4x4.Invert(_camera.ViewProjection, out inverse)
                ? inverse
                : Matrix4x4.Identity;
            _camera.Projection = projection;
            _camera.InverseProjection = Matrix4x4.Invert(_camera.Projection, out inverse)
                ? inverse
                : Matrix4x4.Identity;
            // note: MainViewToProjection not assigned
            _camera.EyePosition = _camera.InverseView.Translation;
            // note: LookAtVector not assigned

            _camera.WorldView = Matrix4x4.Multiply(world, _camera.View);

            _camera.World = world;
            _camera.WorldInverseTranspose = Matrix4x4.Invert(world, out var worldTranspose)
                ? Matrix4x4.Transpose(worldTranspose)
                : Matrix4x4.Identity;
            _camera.WorldViewProjection = Matrix4x4.Multiply(world, _camera.ViewProjection);

            _cameraBufferResource.MarkUpdateRequired();
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct CameraParameters {
        [FieldOffset(0x000)] public Matrix4x4 View;
        [FieldOffset(0x040)] public Matrix4x4 InverseView;
        [FieldOffset(0x080)] public Matrix4x4 ViewProjection;
        [FieldOffset(0x0C0)] public Matrix4x4 InverseViewProjection;
        [FieldOffset(0x100)] public Matrix4x4 Projection;
        [FieldOffset(0x140)] public Matrix4x4 InverseProjection;
        [FieldOffset(0x180)] public Matrix4x4 MainViewToProjection;
        [FieldOffset(0x1C0)] public Vector3 EyePosition;
        [FieldOffset(0x1D0)] public Vector3 LookAtVector;

        [FieldOffset(0x1E0)] public Matrix4x4 WorldView;

        [FieldOffset(0x220)] public Matrix4x4 World;
        [FieldOffset(0x260)] public Matrix4x4 WorldInverseTranspose;
        [FieldOffset(0x2A0)] public Matrix4x4 WorldViewProjection;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DirectionalLight {
        [FieldOffset(0x00)] public Vector3 Direction;
        [FieldOffset(0x10)] public Vector4 Diffuse;
        [FieldOffset(0x20)] public Vector4 Specular;
    };

    [StructLayout(LayoutKind.Explicit)]
    public struct LightParameters {
        [FieldOffset(0x00)] public Vector4 DiffuseColor = Vector4.One;
        [FieldOffset(0x10)] public Vector3 EmissiveColor = Vector3.Zero;
        [FieldOffset(0x20)] public Vector3 AmbientColor = new(0.05333332f, 0.09882354f, 0.1819608f);
        [FieldOffset(0x30)] public Vector3 SpecularColor = Vector3.One;
        [FieldOffset(0x3C)] public float SpecularPower = 64;
        [FieldOffset(0x40)] 
        public DirectionalLight Light0 = new() {
            Direction = new(0.5f, 0.25f, 1),
            Diffuse = Vector4.One,
            Specular = Vector4.One * 0.75f,
        };
        [FieldOffset(0x70)] 
        public DirectionalLight Light1 = new() {
            Direction = new(0, -1, 0),
            Diffuse = Vector4.One,
            Specular = Vector4.One * 0.75f,
        };
        [FieldOffset(0xA0)] 
        public DirectionalLight Light2 = new() {
            Direction = new(-0.5f, 0.25f, -1),
            Diffuse = Vector4.One,
            Specular = Vector4.One * 0.75f,
        };

        public LightParameters() { }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct VsInput {
        [FieldOffset(0x00)] public Vector4 Position;
        [FieldOffset(0x10)] public Vector4 BlendWeight;
        [FieldOffset(0x20)] public Vector4D<byte> BlendIndices;
        [FieldOffset(0x24)] public Vector3 Normal;
        [FieldOffset(0x30)] public Vector4 Uv;
        [FieldOffset(0x40)] public Vector4 Tangent2;
        [FieldOffset(0x50)] public Vector4 Tangent1;
        [FieldOffset(0x60)] public Vector4 Color;
    }
}
