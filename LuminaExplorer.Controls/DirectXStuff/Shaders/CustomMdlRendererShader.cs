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
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;
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
    private ConstantBufferResource<JointMatrixArray> _identityJointMatrixArray;

    public CustomMdlRendererShader(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext) {
        try {
            _pDevice = pDevice;
            _pDevice->AddRef();

            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();

            _identityJointMatrixArray = new(pDevice, pDeviceContext, false, JointMatrixArray.Default);

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
            fixed (uint* pDummy = stackalloc uint[16]) {
                for (var i = 0; i < 16; i++)
                    pDummy[i] = 0xFF000000;
                _dummy = new(_pDevice, Format.FormatR8G8B8A8Unorm, 4, 4, 16, (nint) (&pDummy));
            }
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
        if (disposing) {
            SafeDispose.One(ref _dummy!);
            SafeDispose.One(ref _identityJointMatrixArray!);
        }

        ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public void BindBufferByIndex(uint index, ID3D11Buffer* pBuffer) {
        _pDeviceContext->VSSetConstantBuffers(index, 1u, &pBuffer);
        _pDeviceContext->PSSetConstantBuffers(index, 1u, &pBuffer);
    }

    public void BindCamera(ID3D11Buffer* pBuffer) => BindBufferByIndex(0, pBuffer);
    public void BindWorldViewMatrix(ID3D11Buffer* pBuffer) => BindBufferByIndex(1, pBuffer);
    public void BindJointMatrixArray(ID3D11Buffer* pBuffer) => BindBufferByIndex(2, pBuffer);
    public void BindMiscWorldCamera(ID3D11Buffer* pBuffer) => BindBufferByIndex(3, pBuffer);
    public void BindLight(ID3D11Buffer* pBuffer) => BindBufferByIndex(4, pBuffer);

    public void Draw(ModelObject modelObject, Span<nint> pJointTableBuffers) {
        _pDeviceContext->IASetInputLayout(_pInputLayout);
        _pDeviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);

        _pDeviceContext->VSSetShader(_pVertexShader, null, 0);
        _pDeviceContext->PSSetShader(_pPixelShader, null, 0);
        fixed (ID3D11SamplerState** ppSamplers = _pSamplers)
            _pDeviceContext->PSSetSamplers(0, (uint) _pSamplers.Length, ppSamplers);

        _pDeviceContext->IASetIndexBuffer(modelObject.IndexBuffer, Format.FormatR16Uint, 0);

        for (var i = 0; modelObject.TryGetMesh(i, out var pVertexBuffer, out var boneTableIndex); i++) {
            var submeshes = modelObject.GetSubmeshes(i);
            _pDeviceContext->IASetVertexBuffers(0, 1, pVertexBuffer, (uint) Unsafe.SizeOf<VsInput>(), 0);
            
            if (0 <= boneTableIndex && boneTableIndex < pJointTableBuffers.Length)
                BindJointMatrixArray((ID3D11Buffer*) pJointTableBuffers[boneTableIndex]);
            else
                BindJointMatrixArray(_identityJointMatrixArray.Buffer);

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

        public ModelObject(CustomMdlRendererShader shader, MdlFile mdlFile, int variantId = 1,
            Model.ModelLod lod = Model.ModelLod.High) {
            try {
                // Ensure that we at least have non-null arrays in case of exceptions.
                _meshVertices = new ID3D11Buffer*[0];
                _materials = Array.Empty<Task<Material?>?>();
                _textures = Array.Empty<Task<Texture2DShaderResource?>?[]>();

                _pDevice = shader._pDevice;
                _pDevice->AddRef();
                _mdl = mdlFile;

                _model = new(mdlFile: mdlFile, lod, variantId);
                _materials = new Task<Material?>?[_model.Materials.Length];
                _textures = new Task<Texture2DShaderResource?>[_model.Materials.Length][];

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

        public bool TryGetMesh(int meshIndex, out ID3D11Buffer* pVertexBuffer, out int boneTableIndex) {
            pVertexBuffer = null;
            boneTableIndex = 0;
            if (meshIndex >= _meshVertices.Length || meshIndex < 0)
                return false;

            pVertexBuffer = _meshVertices[meshIndex];
            boneTableIndex = _model.File!.Meshes[_meshes[meshIndex].MeshIndex].BoneTableIndex;
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

                    var mat = new Material(mtrlFile);
                    for (var i = 0; i < _model.Materials.Length; i++)
                        _textures[i] = new Task<Texture2DShaderResource?>[mat?.Textures.Length ?? 0];
                    return mat;
                });

                // Separate this out, since we want the task itself to be in completed state
                // when this callback is called.
                task.ContinueWith(_ => TextureLoadStateChanged?.Invoke());
            }

            if (task is not {IsCompletedSuccessfully: true, Result: { } mat1})
                return false;

            material = mat1;
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

    [StructLayout(LayoutKind.Explicit, Size = 0xC0)]
    public struct WorldMisc {
        [FieldOffset(0x00)] public Matrix4X4<float> World;
        [FieldOffset(0x40)] public Matrix4X4<float> WorldInverseTranspose;
        [FieldOffset(0x80)] public Matrix4X4<float> WorldViewProjection;

        public static WorldMisc FromWorldViewProjection(
            Matrix4x4 world,
            Matrix4x4 view,
            Matrix4x4 projection) {
            var viewProjection = Matrix4x4.Multiply(view, projection);
            return new() {
                World = world.ToSilkValue(),
                WorldInverseTranspose = (Matrix4x4.Invert(world, out var worldTranspose)
                    ? Matrix4x4.Transpose(worldTranspose)
                    : Matrix4x4.Identity).ToSilkValue(),
                WorldViewProjection = Matrix4x4.Multiply(world, viewProjection).ToSilkValue(),
            };
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct DirectionalLight {
        [FieldOffset(0x00)] public Vector3 Direction;
        [FieldOffset(0x10)] public Vector4 Diffuse;
        [FieldOffset(0x20)] public Vector4 Specular;
    };

    [StructLayout(LayoutKind.Explicit)]
    public struct LightParameters {
        [FieldOffset(0x00)] public Vector4 DiffuseColor;
        [FieldOffset(0x10)] public Vector3 EmissiveColor;
        [FieldOffset(0x20)] public Vector3 AmbientColor;
        [FieldOffset(0x30)] public Vector3 SpecularColor;
        [FieldOffset(0x3C)] public float SpecularPower;
        [FieldOffset(0x40)] public DirectionalLight Light0;
        [FieldOffset(0x70)] public DirectionalLight Light1;
        [FieldOffset(0xA0)] public DirectionalLight Light2;

        public static LightParameters Default => new() {
            DiffuseColor = Vector4.One,
            EmissiveColor = Vector3.Zero,
            AmbientColor = new(0.05333332f, 0.09882354f, 0.1819608f),
            SpecularColor = Vector3.One,
            SpecularPower = 64,
            Light0 = new() {
                Direction = new(0.5f, 0.25f, 1),
                Diffuse = Vector4.One,
                Specular = Vector4.One * 0.75f,
            },
            Light1 = new() {
                Direction = new(0, -1, 0),
                Diffuse = Vector4.One,
                Specular = Vector4.One * 0.75f,
            },
            Light2 = new() {
                Direction = new(-0.5f, 0.25f, -1),
                Diffuse = Vector4.One,
                Specular = Vector4.One * 0.75f,
            },
        };
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
