using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Direct3D11;
using ShaderType = LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles.ShaderType;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public unsafe class GameShaderState : DirectXObject {
    private readonly InputId[] _keys;
    private readonly Type[] _types;
    private readonly ID3D11Buffer*[] _buffers;
    private readonly ID3D11Resource*[] _resources;
    private readonly bool[] _needUpdate;
    private ID3D11Device* _pDevice;
    private ID3D11DeviceContext* _pDeviceContext;

    public GameShaderState(GameShaderPool pool) {
        try {
            pool.CopyDeviceAndContext(out _pDevice, out _pDeviceContext);

            _types = InputIdAttribute.FindAllImplementors().ToArray();
            _keys = _types.Select(x => x.GetCustomAttribute<InputIdAttribute>()!.Id).ToArray();
            _buffers = new ID3D11Buffer*[_types.Length];
            _resources = new ID3D11Resource*[_types.Length];
            _needUpdate = new bool[_types.Length];
            Array.Fill(_needUpdate, true);
            for (var i = 0; i < _buffers.Length; i++) {
                fixed (ID3D11Buffer** ppBuffer = &_buffers[i])
                fixed (ID3D11Resource** ppResource = &_resources[i])
                fixed (Guid* pGuid = &ID3D11Resource.Guid) {
                    var bufferDesc = new BufferDesc(
                        byteWidth: (uint) (Marshal.SizeOf(_types[i]) + 15) / 16u * 16u,
                        usage: Usage.Default,
                        bindFlags: (uint) BindFlag.ConstantBuffer,
                        cPUAccessFlags: 0,
                        miscFlags: 0,
                        structureByteStride: 0);
                    ThrowH(_pDevice->CreateBuffer(&bufferDesc, null, ppBuffer));

                    // Note: Using QueryInterface to cast, because I'm not sure if it is by contract you can type
                    //       mangle into ID3D11Resource.
                    ThrowH((*ppBuffer)->QueryInterface(pGuid, (void**) ppResource));
                }
            }
        } catch (Exception) {
            ReleaseUnmanagedResources();
            throw;
        }
    }

    ~GameShaderState() => ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources() {
        for (var i = 0; i < _buffers.Length; i++)
            SafeRelease(ref _buffers[i]);
        for (var i = 0; i < _resources.Length; i++)
            SafeRelease(ref _resources[i]);
        SafeRelease(ref _pDeviceContext);
        SafeRelease(ref _pDevice);
    }

    protected override void Dispose(bool disposing) {
        ReleaseUnmanagedResources();
        base.Dispose(disposing);
    }

    public void MarkUpdateNeeded(InputId key) => _needUpdate[Array.IndexOf(_keys, key)] = true;

    public bool NeedsUpdate(InputId key) => _needUpdate[Array.IndexOf(_keys, key)];

    public void UpdateData<T>(InputId key, T data) where T : unmanaged {
        var i = Array.IndexOf(_keys, key);
        _needUpdate[i] = false;
        _pDeviceContext->UpdateSubresource(_resources[i], 0, null, &data, 0, 0);
    }

    public void UpdateData<T>(T data) where T : unmanaged {
        var i = Array.IndexOf(_types, typeof(T));
        if (i == -1)
            throw new NotSupportedException();

        _needUpdate[i] = false;
        _pDeviceContext->UpdateSubresource(_resources[i], 0, null, &data, 0, 0);
    }

    public void BindConstantBuffersFor(IShaderEntry shaderEntry) {
        fixed (ID3D11Buffer** ppBuffers = _buffers) {
            for (var i = 0; i < shaderEntry.InputTables.Length; i++) {
                var table = shaderEntry.InputTables[i];
                var bufferIndex = Array.IndexOf(_types, table.InternalId);
                if (bufferIndex == -1)
                    continue;
                switch (shaderEntry.ShaderType) {
                    case ShaderType.Pixel:
                        _pDeviceContext->PSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.Vertex:
                        _pDeviceContext->VSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.Geometry:
                        _pDeviceContext->GSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.HullShader:
                        _pDeviceContext->HSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                    case ShaderType.DomainShader:
                        _pDeviceContext->DSSetConstantBuffers((uint) i, 1u, ppBuffers + bufferIndex);
                        break;
                }
            }
        }
    }
}
