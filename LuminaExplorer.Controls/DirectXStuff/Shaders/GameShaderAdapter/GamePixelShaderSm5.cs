using System;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GamePixelShaderSm5 : DirectXObject {
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;
    private ID3D11PixelShader* _pShader;

    public GamePixelShaderSm5(ID3D11Device* pDevice, ID3D11DeviceContext* pDeviceContext, IShaderEntry shaderEntry) {
        if (shaderEntry.InputNames.Length != shaderEntry.InputTables.Length)
            throw new InvalidOperationException();

        try {
            ShaderEntry = shaderEntry;
            _pDevice = pDevice;
            _pDevice->AddRef();
            _pDeviceContext = pDeviceContext;
            _pDeviceContext->AddRef();

            fixed (ID3D11PixelShader** p2 = &_pShader)
            fixed (void* pBytecode = shaderEntry.ByteCode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) shaderEntry.ByteCode.Length, null, p2));
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    ~GamePixelShaderSm5() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
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

    public ID3D11PixelShader* Shader => _pShader;
}
