using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Lumina.Data.Files;
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

public unsafe class CustomMdlRendererShader : DirectXObject {
    private readonly ID3D11SamplerState*[] _pSamplers;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11PixelShader* _pPixelShader;
    private ID3D11VertexShader* _pVertexShader;
    private ID3D11InputLayout* _pInputLayout;
    private Texture2DShaderResource _dummy;

    public CustomMdlRendererShader(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext) {
        try {
            _pDevice = pDevice;
            _pDevice->AddRef();

            _pDeviceContext = pDeviceContext;
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
            fixed (ID3D11SamplerState** ppSamplers = _pSamplers = new ID3D11SamplerState*[5]) {
                for (var i = 0; i < _pSamplers.Length; i++)
                    ThrowH(pDevice->CreateSamplerState(&samplerDesc, ppSamplers + i));
            }

            var bytecode = GetType().CompileShaderFromAssemblyResource("ps_4_0", "main_ps");
            fixed (ID3D11PixelShader** p2 = &_pPixelShader)
            fixed (void* pBytecode = bytecode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2));

            bytecode = GetType().CompileShaderFromAssemblyResource("vs_4_0", "main_vs");
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

            // Some materials refer to dummy.tex; make them point to this.
            fixed (float* pDummy = stackalloc float[16])
                _dummy = new(_pDevice, Format.FormatR8G8B8A8Unorm, 4, 4, 16, (nint) (&pDummy));
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    ~CustomMdlRendererShader() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        for (var i = 0; i < _pSamplers.Length; i++)
            SafeRelease(ref _pSamplers[i]);
        SafeRelease(ref _pPixelShader);
        SafeRelease(ref _pVertexShader);
        SafeRelease(ref _pInputLayout);
        SafeRelease(ref _pDevice);
        SafeRelease(ref _pDeviceContext);
    }

    private void DisposePrivate(bool disposing) {
        if (disposing)
            SafeDispose.One(ref _dummy!);
        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(disposing);
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
                    if (!modelObject.TryGetTexture(materialIndex, j, out var pTexture))
                        pTexture = _dummy.ShaderResourceView;

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

    public class ModelObject : DirectXObject {
        private readonly MdlFile _mdl;
        private readonly Model _model;
        private readonly Mesh[] _meshes;
        private readonly Task<Material?>?[] _materials;
        private readonly ID3D11Buffer*[] _meshVertices;
        private Task<Texture2DShaderResource?>?[ /* Material Index*/][ /* Texture Index */] _textures;
        private ID3D11Device* _pDevice;
        private ID3D11Buffer* _pIndexBuffer;

        public ModelObject(CustomMdlRendererShader shader, MdlFile mdlFile, int variantId = 1, Model.ModelLod lod = Model.ModelLod.High) {
            try {
                _pDevice = shader._pDevice;
                _pDevice->AddRef();
                _mdl = mdlFile;

                _model = new(mdlFile: mdlFile, lod, variantId);
                _materials = new Task<Material?>?[_model.Materials.Length];
                _textures = new Task<Texture2DShaderResource?>[_model.Materials.Length][];

                for (var i = 0; i < _model.Materials.Length; i++) {
                    var mat = (Material?) _model.Materials[i];
                    _textures[i] = new Task<Texture2DShaderResource?>[mat?.Textures.Length ?? 0];
                }

                _meshes = _model.Meshes
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

                fixed (void* pIndexData = &_mdl.Data[_mdl.FileHeader.IndexOffset[(int) _model.Lod]])
                fixed (ID3D11Buffer** ppIndexBuffer = &_pIndexBuffer) {
                    var indexData = new SubresourceData(pIndexData);
                    var indexBufferDesc = new BufferDesc(
                        _mdl.FileHeader.IndexBufferSize[(int) _model.Lod],
                        Usage.Default,
                        (uint) BindFlag.IndexBuffer);
                    ThrowH(shader._pDevice->CreateBuffer(&indexBufferDesc, &indexData, ppIndexBuffer));
                }
            } catch (Exception) {
                DisposeInner(true);
                throw;
            }
        }

        ~ModelObject() => ReleaseUnmanagedResources();

        private void ReleaseUnmanagedResources() {
            for (var i = 0; i < _meshVertices.Length; i++)
                SafeRelease(ref _meshVertices[i]);
            SafeRelease(ref _pIndexBuffer);
            SafeRelease(ref _pDevice);
        }

        private void DisposeInner(bool disposing) {
            if (disposing)
                SafeDispose.Enumerable(ref _textures!);

            ReleaseUnmanagedResources();
        }

        protected override void Dispose(bool disposing) {
            DisposeInner(disposing);
            base.Dispose(disposing);
        }

        public event ShaderEvents.FileRequested<DdsFile>? DdsFileRequested;

        public event ShaderEvents.FileRequested<MtrlFile>? MtrlFileRequested;

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
            material = null!;

            var task = _materials[materialIndex];
            if (task is null) {
                if (MtrlFileRequested is null)
                    return false;

                var mtrlPathSpan = _mdl.Strings.AsSpan((int) _mdl.MaterialNameOffsets[materialIndex]);
                mtrlPathSpan = mtrlPathSpan[..mtrlPathSpan.IndexOf((byte) 0)];
                
                var mtrlPath = Encoding.UTF8.GetString(mtrlPathSpan);
                if (mtrlPath.StartsWith('/')) {
                    mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, _model.VariantId);
                    if (mtrlPath is null) {
                        _materials[materialIndex] = Task.FromResult((Material?) null);
                        return false;
                    }
                }

                Task<MtrlFile?>? loader = null;
                MtrlFileRequested?.Invoke(mtrlPath, ref loader);
                if (loader is null)
                    return false;

                _materials[materialIndex] = task = loader.ContinueWith(r => {
                    if (!r.IsCompletedSuccessfully || r.Result is not { } mtrlFile)
                        return null;
                    return new Material(mtrlFile);
                });
            }

            if (task is not {IsCompletedSuccessfully: true, Result: { } mat})
                return false;

            material = mat;
            return true;
        }

        public bool TryGetTexture(int materialIndex, int textureIndex, out ID3D11ShaderResourceView* pTexture) {
            pTexture = null;
            if (_materials[materialIndex] is not {IsCompletedSuccessfully: true, Result: { } mat})
                return false;

            var task = _textures[materialIndex][textureIndex];
            if (task is null) {
                if (DdsFileRequested is null)
                    return false;

                var textureDefinition = mat.Textures[textureIndex];
                if (textureDefinition.TexturePath == "dummy.tex") {
                    _textures[materialIndex][textureIndex] = Task.FromResult((Texture2DShaderResource?) null);
                    return false;
                }

                Task<DdsFile?>? loader = null;
                DdsFileRequested?.Invoke(textureDefinition.TexturePath, ref loader);
                if (loader is null)
                    return false;

                var pDevice = _pDevice;
                pDevice->AddRef();
                _textures[materialIndex][textureIndex] = task = loader.ContinueWith(r => {
                    try {
                        if (!r.IsCompletedSuccessfully || r.Result is not { } ddsFile)
                            return null;
                        return new Texture2DShaderResource(pDevice, ddsFile);
                    } finally {
                        pDevice->Release();
                    }
                });

                // Separate this out, since we want the task itself to be in completed state
                // when this callback is called.
                task.ContinueWith(_ => TextureLoadStateChanged?.Invoke());
            }

            if (task is {IsCompletedSuccessfully: true, Result: { } result}) {
                pTexture = result.ShaderResourceView;
                return true;
            }

            pTexture = null;
            return false;
        }

        public int GetNumIndices(int i) => _meshes[i].Indices.Length;

        public Submesh[] GetSubmeshes(int i) => _meshes[i].Submeshes;
    }

    public sealed class State : IDisposable {
        private ID3D11DeviceContext* _pDeviceContext;
        private ConstantBufferResource<CameraParameters> _cameraBufferResource;
        private ConstantBufferResource<LightParameters> _lightBufferResource;
        private readonly ID3D11Buffer*[] _buffers = new ID3D11Buffer*[2];

        private CameraParameters _camera = new();
        private LightParameters _light = new();

        public State(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext) {
            try {
                _pDeviceContext = pDeviceContext;
                _pDeviceContext->AddRef();
                _cameraBufferResource = new(pDevice, pDeviceContext);
                _lightBufferResource = new(pDevice, pDeviceContext);

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

            SafeRelease(ref _pDeviceContext);
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

                _pDeviceContext->VSSetConstantBuffers(0, (uint) _buffers.Length, ppBuffers);
                _pDeviceContext->PSSetConstantBuffers(0, (uint) _buffers.Length, ppBuffers);
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
        [FieldOffset(0x000)] public Matrix4x4 View = Matrix4x4.Identity;
        [FieldOffset(0x040)] public Matrix4x4 InverseView = Matrix4x4.Identity;
        [FieldOffset(0x080)] public Matrix4x4 ViewProjection = Matrix4x4.Identity;
        [FieldOffset(0x0C0)] public Matrix4x4 InverseViewProjection = Matrix4x4.Identity;
        [FieldOffset(0x100)] public Matrix4x4 Projection = Matrix4x4.Identity;
        [FieldOffset(0x140)] public Matrix4x4 InverseProjection = Matrix4x4.Identity;
        [FieldOffset(0x180)] public Matrix4x4 MainViewToProjection = Matrix4x4.Identity;
        [FieldOffset(0x1C0)] public Vector3 EyePosition = Vector3.Zero;
        [FieldOffset(0x1D0)] public Vector3 LookAtVector = Vector3.Zero;

        [FieldOffset(0x1E0)] public Matrix4x4 WorldView = Matrix4x4.Identity;

        [FieldOffset(0x220)] public Matrix4x4 World = Matrix4x4.Identity;
        [FieldOffset(0x260)] public Matrix4x4 WorldInverseTranspose = Matrix4x4.Identity;
        [FieldOffset(0x2A0)] public Matrix4x4 WorldViewProjection = Matrix4x4.Identity;

        public CameraParameters() { }
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

        [FieldOffset(0x40)] public DirectionalLight Light0 = new() {
            Direction = new(0.5f, 0.25f, 1),
            Diffuse = Vector4.One,
            Specular = Vector4.One * 0.75f,
        };

        [FieldOffset(0x70)] public DirectionalLight Light1 = new() {
            Direction = new(0, -1, 0),
            Diffuse = Vector4.One,
            Specular = Vector4.One * 0.75f,
        };

        [FieldOffset(0xA0)] public DirectionalLight Light2 = new() {
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
