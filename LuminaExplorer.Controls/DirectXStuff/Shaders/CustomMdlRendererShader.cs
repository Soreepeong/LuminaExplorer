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
using Silk.NET.Maths;
using TerraFX.Interop.DirectX;

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

    public CustomMdlRendererShader(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext)
    {
        try {
            this._pDevice = pDevice;
            this._pDevice->AddRef();

            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();

            this._identityJointMatrixArray = new(pDevice, pDeviceContext, false, JointMatrixArray.Default);

            var samplerDesc = new D3D11_SAMPLER_DESC(
                filter: D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                addressU: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                addressV: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                addressW: D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_WRAP,
                mipLODBias: 0,
                maxAnisotropy: 0,
                comparisonFunc: D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                borderColor: null,
                minLOD: 0,
                maxLOD: float.MaxValue);
            fixed (ID3D11SamplerState** ppSamplers = this._pSamplers = new ID3D11SamplerState*[5]) {
                for (var i = 0; i < this._pSamplers.Length; i++)
                    pDevice->CreateSamplerState(&samplerDesc, ppSamplers + i).Ensure();
            }

            var bytecode = this.GetType().CompileShaderFromAssemblyResource("ps_4_0", "main_ps");
            fixed (ID3D11PixelShader** p2 = &this._pPixelShader)
            fixed (void* pBytecode = bytecode)
                pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2).Ensure();

            bytecode = this.GetType().CompileShaderFromAssemblyResource("vs_4_0", "main_vs");
            fixed (ID3D11VertexShader** ppVertexShader = &this._pVertexShader)
            fixed (ID3D11InputLayout** ppInputLayout = &this._pInputLayout)
            fixed (byte* pBytecode = bytecode)
            fixed (byte* pszPosition = "POSITION"u8)
            fixed (byte* pszNormal = "NORMAL"u8)
            fixed (byte* pszTexCoord = "TEXCOORD"u8)
            fixed (byte* pszBlendWeight = "BLENDWEIGHT"u8)
            fixed (byte* pszBlendIndices = "BLENDINDICES"u8)
            fixed (byte* pszColor = "COLOR"u8)
            fixed (byte* pszTangent = "TANGENT"u8) {
                pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, ppVertexShader).Ensure();

                const int numDesc = 8;
                var desc = stackalloc D3D11_INPUT_ELEMENT_DESC[numDesc];
                desc[0].SetVertex(pszPosition, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);
                desc[1].SetVertex(pszBlendWeight, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);
                desc[2].SetVertex(pszBlendIndices, 0, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT);
                desc[3].SetVertex(pszNormal, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT);
                desc[4].SetVertex(pszTexCoord, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);
                desc[5].SetVertex(pszTangent, 1, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);
                desc[6].SetVertex(pszTangent, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);
                desc[7].SetVertex(pszColor, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT);

                this._pDevice->CreateInputLayout(desc, numDesc, pBytecode, (nuint) bytecode.Length, ppInputLayout)
                    .Ensure();
            }

            // Some materials refer to dummy.tex; make them point to this.
            fixed (uint* pDummy = stackalloc uint[16]) {
                for (var i = 0; i < 16; i++)
                    pDummy[i] = 0xFF000000;
                this._dummy = new(this._pDevice, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, 4, 4, 16, (nint) (&pDummy));
            }
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~CustomMdlRendererShader() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        for (var i = 0; i < this._pSamplers.Length; i++)
            SafeRelease(ref this._pSamplers[i]);
        SafeRelease(ref this._pPixelShader);
        SafeRelease(ref this._pVertexShader);
        SafeRelease(ref this._pInputLayout);
        SafeRelease(ref this._pDevice);
        SafeRelease(ref this._pDeviceContext);
    }

    private void DisposePrivate(bool disposing)
    {
        if (disposing) {
            SafeDispose.One(ref this._dummy!);
            SafeDispose.One(ref this._identityJointMatrixArray!);
        }

        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public void BindBufferByIndex(uint index, ID3D11Buffer* pBuffer)
    {
        this._pDeviceContext->VSSetConstantBuffers(index, 1u, &pBuffer);
        this._pDeviceContext->PSSetConstantBuffers(index, 1u, &pBuffer);
    }

    public void BindCamera(ID3D11Buffer* pBuffer) => this.BindBufferByIndex(0, pBuffer);
    public void BindWorldViewMatrix(ID3D11Buffer* pBuffer) => this.BindBufferByIndex(1, pBuffer);
    public void BindJointMatrixArray(ID3D11Buffer* pBuffer) => this.BindBufferByIndex(2, pBuffer);
    public void BindMiscWorldCamera(ID3D11Buffer* pBuffer) => this.BindBufferByIndex(3, pBuffer);
    public void BindLight(ID3D11Buffer* pBuffer) => this.BindBufferByIndex(4, pBuffer);

    public void Draw(ModelObject modelObject, Span<nint> pJointTableBuffers)
    {
        this._pDeviceContext->IASetInputLayout(this._pInputLayout);
        this._pDeviceContext->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        this._pDeviceContext->VSSetShader(this._pVertexShader, null, 0);
        this._pDeviceContext->PSSetShader(this._pPixelShader, null, 0);
        fixed (ID3D11SamplerState** ppSamplers =
                   this._pSamplers) this._pDeviceContext->PSSetSamplers(0, (uint) this._pSamplers.Length, ppSamplers);

        this._pDeviceContext->IASetIndexBuffer(modelObject.IndexBuffer, DXGI_FORMAT.DXGI_FORMAT_R16_UINT, 0);

        var stride = (uint) sizeof(VsInput);
        var offset = 0u;

        for (var i = 0; modelObject.TryGetMesh(i, out var pVertexBuffer, out var boneTableIndex); i++) {
            var submeshes = modelObject.GetSubmeshes(i);
            this._pDeviceContext->IASetVertexBuffers(0, 1, &pVertexBuffer, &stride, &offset);

            if (0 <= boneTableIndex && boneTableIndex < pJointTableBuffers.Length)
                this.BindJointMatrixArray((ID3D11Buffer*) pJointTableBuffers[boneTableIndex]);
            else
                this.BindJointMatrixArray(this._identityJointMatrixArray.Buffer);

            if (modelObject.TryGetMaterial(i, out var materialIndex, out var material)) {
                for (var j = 0; j < material.Textures.Length; j++) {
                    var t = material.Textures[j];
                    if (!modelObject.TryGetTexture(materialIndex, j, out var pTexture))
                        pTexture = this._dummy.ShaderResourceView;

                    switch (t.TextureUsageSimple) {
                        case Texture.Usage.Diffuse:
                            this._pDeviceContext->PSSetShaderResources(0, 1, &pTexture);
                            break;
                        case Texture.Usage.Normal:
                            this._pDeviceContext->PSSetShaderResources(1, 1, &pTexture);
                            break;
                        case Texture.Usage.Specular:
                            this._pDeviceContext->PSSetShaderResources(2, 1, &pTexture);
                            break;
                        case Texture.Usage.Mask:
                            this._pDeviceContext->PSSetShaderResources(3, 1, &pTexture);
                            break;
                    }
                }
            }

            if (submeshes.Any()) {
                foreach (var submesh in submeshes)
                    this._pDeviceContext->DrawIndexed(submesh.IndexNum, submesh.IndexOffset, 0);
            } else {
                this._pDeviceContext->DrawIndexed((uint) modelObject.GetNumIndices(i), 0, 0);
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

        public ModelObject(
            CustomMdlRendererShader shader,
            MdlFile mdlFile,
            int variantId = 1,
            Model.ModelLod lod = Model.ModelLod.High)
        {
            try {
                // Ensure that we at least have non-null arrays in case of exceptions.
                this._meshVertices = [];
                this._materials = [];
                this._textures = [];

                this._pDevice = shader._pDevice;
                this._pDevice->AddRef();
                this._mdl = mdlFile;

                this._model = new(mdlFile: mdlFile, lod, variantId);
                this._materials = new Task<Material?>?[this._model.Materials.Length];
                this._textures = new Task<Texture2DShaderResource?>[this._model.Materials.Length][];

                this._meshes = this._model.Meshes
                    .Where(x => x.Types.Contains(Mesh.MeshType.Main))
                    .Where(x => x.Vertices.Any())
                    .ToArray();
                this._meshVertices = new ID3D11Buffer*[this._meshes.Length];
                for (var i = 0; i < this._meshes.Length; i++) {
                    var mesh = this._meshes[i];
                    var vertices = new VsInput[mesh.Vertices.Length];
                    for (var j = 0; j < mesh.Vertices.Length; j++) {
                        var source = mesh.Vertices[j];
                        vertices[j] = new() {
                            Position = (source.Position ?? Vector4.Zero) with { W = 1f },
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
                    fixed (ID3D11Buffer** ppVertexBuffer = &this._meshVertices[i]) {
                        var vertexData = new D3D11_SUBRESOURCE_DATA { pSysMem = pVertices };
                        var vertexBufferDesc = new D3D11_BUFFER_DESC(
                            (uint) (Unsafe.SizeOf<VsInput>() * vertices.Length),
                            (uint) D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
                        shader._pDevice->CreateBuffer(&vertexBufferDesc, &vertexData, ppVertexBuffer).Ensure();
                    }
                }

                fixed (void* pIndexData = &this._mdl.Data[this._mdl.FileHeader.IndexOffset[(int) this._model.Lod]])
                fixed (ID3D11Buffer** ppIndexBuffer = &this._pIndexBuffer) {
                    var indexData = new D3D11_SUBRESOURCE_DATA { pSysMem = pIndexData };
                    var indexBufferDesc = new D3D11_BUFFER_DESC(
                        this._mdl.FileHeader.IndexBufferSize[(int) this._model.Lod],
                        (uint) D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);
                    shader._pDevice->CreateBuffer(&indexBufferDesc, &indexData, ppIndexBuffer).Ensure();
                }
            } catch (Exception) {
                this.DisposeInner(true);
                throw;
            }
        }

        ~ModelObject() => this.ReleaseUnmanagedResources();

        private void ReleaseUnmanagedResources()
        {
            for (var i = 0; i < this._meshVertices.Length; i++)
                SafeRelease(ref this._meshVertices[i]);
            SafeRelease(ref this._pIndexBuffer);
            SafeRelease(ref this._pDevice);
        }

        private void DisposeInner(bool disposing)
        {
            if (disposing)
                SafeDispose.Enumerable(ref this._textures!);

            this.ReleaseUnmanagedResources();
        }

        protected override void Dispose(bool disposing)
        {
            this.DisposeInner(disposing);
            base.Dispose(disposing);
        }

        public event ShaderEvents.FileRequested<DdsFile>? DdsFileRequested;

        public event ShaderEvents.FileRequested<MtrlFile>? MtrlFileRequested;

        public event Action? TextureLoadStateChanged;

        public ID3D11Buffer* IndexBuffer => this._pIndexBuffer;

        public bool TryGetMesh(int meshIndex, out ID3D11Buffer* pVertexBuffer, out int boneTableIndex)
        {
            pVertexBuffer = null;
            boneTableIndex = 0;
            if (meshIndex >= this._meshVertices.Length || meshIndex < 0)
                return false;

            pVertexBuffer = this._meshVertices[meshIndex];
            boneTableIndex = this._model.File!.Meshes[this._meshes[meshIndex].MeshIndex].BoneTableIndex;
            return true;
        }

        public bool TryGetMaterial(int meshIndex, out int materialIndex, [MaybeNullWhen(false)] out Material material)
        {
            materialIndex = this._model.File!.Meshes[this._meshes[meshIndex].MeshIndex].MaterialIndex;
            material = null!;

            var task = this._materials[materialIndex];
            if (task is null) {
                if (this.MtrlFileRequested is null)
                    return false;

                var mtrlPathSpan = this._mdl.Strings.AsSpan((int) this._mdl.MaterialNameOffsets[materialIndex]);
                mtrlPathSpan = mtrlPathSpan[..mtrlPathSpan.IndexOf((byte) 0)];

                var mtrlPath = Encoding.UTF8.GetString(mtrlPathSpan);
                if (mtrlPath.StartsWith('/')) {
                    mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, this._model.VariantId);
                    if (mtrlPath is null) {
                        this._materials[materialIndex] = Task.FromResult((Material?) null);
                        return false;
                    }
                }

                Task<MtrlFile?>? loader = null;
                this.MtrlFileRequested?.Invoke(mtrlPath, ref loader);
                if (loader is null)
                    return false;

                this._materials[materialIndex] = task = loader.ContinueWith(
                    r => {
                        if (!r.IsCompletedSuccessfully || r.Result is not { } mtrlFile)
                            return null;

                        var mat = new Material(mtrlFile);
                        for (var i = 0; i < this._model.Materials.Length; i++)
                            this._textures[i] = new Task<Texture2DShaderResource?>[mat?.Textures.Length ?? 0];
                        return mat;
                    });

                // Separate this out, since we want the task itself to be in completed state
                // when this callback is called.
                task.ContinueWith(_ => this.TextureLoadStateChanged?.Invoke());
            }

            if (task is not { IsCompletedSuccessfully: true, Result: { } mat1 })
                return false;

            material = mat1;
            return true;
        }

        public bool TryGetTexture(int materialIndex, int textureIndex, out ID3D11ShaderResourceView* pTexture)
        {
            pTexture = null;
            if (this._materials[materialIndex] is not { IsCompletedSuccessfully: true, Result: { } mat })
                return false;

            var task = this._textures[materialIndex][textureIndex];
            if (task is null) {
                if (this.DdsFileRequested is null)
                    return false;

                var textureDefinition = mat.Textures[textureIndex];
                if (textureDefinition.TexturePath == "dummy.tex") {
                    this._textures[materialIndex][textureIndex] = Task.FromResult((Texture2DShaderResource?) null);
                    return false;
                }

                Task<DdsFile?>? loader = null;
                this.DdsFileRequested?.Invoke(textureDefinition.TexturePath, ref loader);
                if (loader is null)
                    return false;

                var pDevice = this._pDevice;
                pDevice->AddRef();
                this._textures[materialIndex][textureIndex] = task = loader.ContinueWith(
                    r => {
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
                task.ContinueWith(_ => this.TextureLoadStateChanged?.Invoke());
            }

            if (task is { IsCompletedSuccessfully: true, Result: { } result }) {
                pTexture = result.ShaderResourceView;
                return true;
            }

            pTexture = null;
            return false;
        }

        public int GetNumIndices(int i) => this._meshes[i].Indices.Length;

        public Submesh[] GetSubmeshes(int i) => this._meshes[i].Submeshes;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xC0)]
    public struct WorldMisc {
        [FieldOffset(0x00)] public Matrix4X4<float> World;
        [FieldOffset(0x40)] public Matrix4X4<float> WorldInverseTranspose;
        [FieldOffset(0x80)] public Matrix4X4<float> WorldViewProjection;

        public static WorldMisc FromWorldViewProjection(
            Matrix4x4 world,
            Matrix4x4 view,
            Matrix4x4 projection)
        {
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
