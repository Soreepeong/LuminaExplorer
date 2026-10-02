using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using ShaderType = LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles.ShaderType;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>
/// A shader from a shader package, along with the information needed to bind its resources.
/// </summary>
/// <remarks>
/// Resources are identified by the CRC32 of their names, which are the same as the IDs used in shader packages and
/// materials. The names are taken from the reflection data embedded in the bytecode; the shader package's own resource
/// tables are used as the fallback.
/// </remarks>
public abstract unsafe class GameShaderSm5 : DirectXObject {
    protected GameShaderSm5(IShaderEntry shaderEntry)
    {
        this.ShaderEntry = shaderEntry;
        this.ByteCode = shaderEntry.ByteCode.ToArray();

        var constants = new List<ResourceBinding>();
        var samplers = new List<ResourceBinding>();
        var textures = new List<ResourceBinding>();
        var inputs = new List<InputParameter>();
        if (!this.TryReflect(constants, samplers, textures, inputs)) {
            constants.Clear();
            samplers.Clear();
            textures.Clear();
            inputs.Clear();
            for (var i = 0; i < shaderEntry.InputTables.Length; i++) {
                var table = shaderEntry.InputTables[i];
                if (table.RegisterIndex == ushort.MaxValue)
                    continue;

                var binding = new ResourceBinding(
                    (uint) table.InternalId,
                    shaderEntry.InputNames[i],
                    table.RegisterIndex,
                    table.RegisterCount * 16,
                    D3D_SRV_DIMENSION.D3D_SRV_DIMENSION_TEXTURE2D);
                if (i < shaderEntry.Header.ConstantCount)
                    constants.Add(binding);
                else if (i < shaderEntry.Header.ConstantCount + shaderEntry.Header.SamplerCount)
                    samplers.Add(binding);
                else if (i >= shaderEntry.Header.ConstantCount + shaderEntry.Header.SamplerCount +
                         shaderEntry.Header.UavCount)
                    textures.Add(binding);
            }
        }

        this.Constants = constants.ToArray();
        this.Samplers = samplers.ToArray();
        this.Textures = textures.ToArray();
        this.InputParameters = inputs.ToArray();
    }

    public IShaderEntry ShaderEntry { get; }

    public byte[] ByteCode { get; }

    public ShaderType ShaderType => this.ShaderEntry.ShaderType;

    /// <summary>Constant buffers used by this shader.</summary>
    public ResourceBinding[] Constants { get; }

    /// <summary>Sampler states used by this shader.</summary>
    public ResourceBinding[] Samplers { get; }

    /// <summary>Shader resource views (textures and buffers) used by this shader.</summary>
    public ResourceBinding[] Textures { get; }

    /// <summary>Input signature, excluding system values. Only filled for vertex shaders.</summary>
    public InputParameter[] InputParameters { get; }

    private bool TryReflect(
        List<ResourceBinding> constants,
        List<ResourceBinding> samplers,
        List<ResourceBinding> textures,
        List<InputParameter> inputs)
    {
        using var reflection = default(ComPtr<ID3D11ShaderReflection>);
        fixed (byte* pBytecode = this.ByteCode)
        fixed (Guid* piid = &IID.IID_ID3D11ShaderReflection) {
            if (DirectX.D3DReflect(pBytecode, (nuint) this.ByteCode.Length, piid, (void**) reflection.GetAddressOf())
                .FAILED)
                return false;
        }

        D3D11_SHADER_DESC desc;
        if (reflection.Get()->GetDesc(&desc).FAILED)
            return false;

        for (var i = 0u; i < desc.BoundResources; i++) {
            D3D11_SHADER_INPUT_BIND_DESC bindDesc;
            if (reflection.Get()->GetResourceBindingDesc(i, &bindDesc).FAILED)
                return false;

            var name = ReadString(bindDesc.Name);
            if (name.EndsWith(".T") || name.EndsWith(".S"))
                name = name[..^2];
            var id = GameShaderIds.Crc(name);

            switch (bindDesc.Type) {
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_CBUFFER: {
                    var size = 0;
                    var cb = reflection.Get()->GetConstantBufferByName(bindDesc.Name);
                    D3D11_SHADER_BUFFER_DESC cbDesc;
                    if (cb is not null && cb->GetDesc(&cbDesc).SUCCEEDED)
                        size = (int) cbDesc.Size;
                    constants.Add(new(id, name, bindDesc.BindPoint, size, bindDesc.Dimension));
                    break;
                }
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_SAMPLER:
                    samplers.Add(new(id, name, bindDesc.BindPoint, 0, bindDesc.Dimension));
                    break;
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_TEXTURE:
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_STRUCTURED:
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_BYTEADDRESS:
                case D3D_SHADER_INPUT_TYPE.D3D_SIT_TBUFFER:
                    textures.Add(new(id, name, bindDesc.BindPoint, 0, bindDesc.Dimension));
                    break;
            }
        }

        if (this.ShaderType == ShaderType.Vertex) {
            for (var i = 0u; i < desc.InputParameters; i++) {
                D3D11_SIGNATURE_PARAMETER_DESC paramDesc;
                if (reflection.Get()->GetInputParameterDesc(i, &paramDesc).FAILED)
                    return false;
                if (paramDesc.SystemValueType != D3D_NAME.D3D_NAME_UNDEFINED)
                    continue;
                inputs.Add(
                    new(ReadString(paramDesc.SemanticName), paramDesc.SemanticIndex, paramDesc.ComponentType));
            }
        }

        return true;
    }

    private static string ReadString(sbyte* psz) =>
        psz is null ? string.Empty : Marshal.PtrToStringUTF8((nint) psz) ?? string.Empty;

    public readonly record struct ResourceBinding(
        uint Id,
        string Name,
        uint Slot,
        int Size,
        D3D_SRV_DIMENSION Dimension);

    /// <summary>An input of a vertex shader.</summary>
    /// <param name="SemanticName">Semantic name.</param>
    /// <param name="SemanticIndex">Semantic index.</param>
    /// <param name="ComponentType">Type of the register components. Skinning vertex shaders read BLENDWEIGHT and
    /// BLENDINDICES as integers, and unpack them by themselves.</param>
    public readonly record struct InputParameter(
        string SemanticName,
        uint SemanticIndex,
        D3D_REGISTER_COMPONENT_TYPE ComponentType = D3D_REGISTER_COMPONENT_TYPE.D3D_REGISTER_COMPONENT_FLOAT32);
}

public sealed unsafe class GameVertexShaderSm5 : GameShaderSm5 {
    private ID3D11VertexShader* _pShader;

    public GameVertexShaderSm5(ID3D11Device* pDevice, IShaderEntry shaderEntry) : base(shaderEntry)
    {
        fixed (ID3D11VertexShader** p2 = &this._pShader)
        fixed (void* pBytecode = this.ByteCode)
            pDevice->CreateVertexShader(pBytecode, (nuint) this.ByteCode.Length, null, p2).Ensure();
    }

    ~GameVertexShaderSm5() => this.ReleaseUnmanagedResources();

    public ID3D11VertexShader* Shader => this._pShader;

    private void ReleaseUnmanagedResources() => SafeRelease(ref this._pShader);

    protected override void Dispose(bool disposing)
    {
        this.ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }
}

public sealed unsafe class GamePixelShaderSm5 : GameShaderSm5 {
    private ID3D11PixelShader* _pShader;

    public GamePixelShaderSm5(ID3D11Device* pDevice, IShaderEntry shaderEntry) : base(shaderEntry)
    {
        fixed (ID3D11PixelShader** p2 = &this._pShader)
        fixed (void* pBytecode = this.ByteCode)
            pDevice->CreatePixelShader(pBytecode, (nuint) this.ByteCode.Length, null, p2).Ensure();
    }

    ~GamePixelShaderSm5() => this.ReleaseUnmanagedResources();

    public ID3D11PixelShader* Shader => this._pShader;

    private void ReleaseUnmanagedResources() => SafeRelease(ref this._pShader);

    protected override void Dispose(bool disposing)
    {
        this.ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }
}
