using System;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GamePixelShaderSm5 : DirectXObject {
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11PixelShader* _pShader;

    public GamePixelShaderSm5(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext, IShaderEntry shaderEntry)
    {
        if (shaderEntry.InputNames.Length != shaderEntry.InputTables.Length)
            throw new InvalidOperationException();

        try {
            this.ShaderEntry = shaderEntry;
            this._pDevice = pDevice;
            this._pDevice->AddRef();
            this._pDeviceContext = pDeviceContext;
            this._pDeviceContext->AddRef();

            fixed (ID3D11PixelShader** p2 = &this._pShader)
            fixed (void* pBytecode = shaderEntry.ByteCode)
                pDevice->CreatePixelShader(pBytecode, (nuint) shaderEntry.ByteCode.Length, null, p2).Ensure();
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    ~GamePixelShaderSm5() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
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

    public ID3D11PixelShader* Shader => this._pShader;
}
