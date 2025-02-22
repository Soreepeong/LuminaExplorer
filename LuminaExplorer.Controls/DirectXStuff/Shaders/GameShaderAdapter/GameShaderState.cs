using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using ShaderType = LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles.ShaderType;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GameShaderState : DirectXObject {
    private readonly InputId[] _keys;
    private readonly Type[] _types;
    private readonly ID3D11Buffer*[] _buffers;
    private readonly bool[] _needUpdate;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;

    public GameShaderState(GameShaderPool pool)
    {
        try {
            pool.CopyDeviceAndContext(out this._pDevice, out this._pDeviceContext);

            this._types = InputIdAttribute.FindAllImplementors().ToArray();
            this._keys = this._types.Select(x => x.GetCustomAttribute<InputIdAttribute>()!.Id).ToArray();
            this._buffers = new ID3D11Buffer*[this._types.Length];
            this._needUpdate = new bool[this._types.Length];
            Array.Fill(this._needUpdate, true);
            for (var i = 0; i < this._buffers.Length; i++) {
                fixed (ID3D11Buffer** ppBuffer = &this._buffers[i])
                fixed (Guid* pGuid = &IID.IID_ID3D11Resource) {
                    var bufferDesc = new D3D11_BUFFER_DESC(
                        byteWidth: (uint) (Marshal.SizeOf(this._types[i]) + 15u) / 16u * 16u,
                        bindFlags: (uint) D3D11_BIND_FLAG.D3D11_BIND_CONSTANT_BUFFER);
                    this._pDevice->CreateBuffer(&bufferDesc, null, ppBuffer).Ensure();
                }
            }
        } catch (Exception) {
            this.ReleaseUnmanagedResources();
            throw;
        }
    }

    ~GameShaderState() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        for (var i = 0; i < this._buffers.Length; i++)
            SafeRelease(ref this._buffers[i]);
        SafeRelease(ref this._pDeviceContext);
        SafeRelease(ref this._pDevice);
    }

    protected override void Dispose(bool disposing)
    {
        this.ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }

    public void MarkUpdateNeeded(InputId key) => this._needUpdate[Array.IndexOf(this._keys, key)] = true;

    public bool NeedsUpdate(InputId key) => this._needUpdate[Array.IndexOf(this._keys, key)];

    public void UpdateData<T>(InputId key, T data) where T : unmanaged
    {
        var i = Array.IndexOf(this._keys, key);
        this._needUpdate[i] = false;
        this._pDeviceContext->UpdateSubresource((ID3D11Resource*) this._buffers[i], 0, null, &data, 0, 0);
    }

    public void UpdateData<T>(T data) where T : unmanaged
    {
        var i = Array.IndexOf(this._types, typeof(T));
        if (i == -1)
            throw new NotSupportedException();

        this._needUpdate[i] = false;
        this._pDeviceContext->UpdateSubresource((ID3D11Resource*) this._buffers[i], 0, null, &data, 0, 0);
    }

    public void BindConstantBuffersFor(IShaderEntry shaderEntry)
    {
        fixed (ID3D11Buffer** ppBuffers = this._buffers) {
            for (var i = 0; i < shaderEntry.InputTables.Length; i++) {
                var table = shaderEntry.InputTables[i];
                var bufferIndex = Array.IndexOf(this._types, table.InternalId);
                if (bufferIndex == -1)
                    continue;
                switch (shaderEntry.ShaderType) {
                    case ShaderType.Pixel:
                        this._pDeviceContext->PSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.Vertex:
                        this._pDeviceContext->VSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.Geometry:
                        this._pDeviceContext->GSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.HullShader:
                        this._pDeviceContext->HSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.DomainShader:
                        this._pDeviceContext->DSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                }
            }
        }
    }
}
