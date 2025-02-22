using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public sealed unsafe class DirectXTexRendererShader : DirectXObject {
    private static readonly float[,,] VerticesAndTexCoords = {
        // { { x, y }, { u, v } }
        { { 0, 0 }, { 0, 0 } },
        { { 0, 1 }, { 0, 1 } },
        { { 1, 0 }, { 1, 0 } },
        { { 1, 1 }, { 1, 1 } },
    };

    private static readonly ushort[,] Indices = {
        { 0, 1, 2 },
        { 3, 2, 1 },
    };

    private static readonly uint[] InputStrides = [
        (uint) (Buffer.ByteLength(VerticesAndTexCoords) / VerticesAndTexCoords.GetLength(0)),
        (uint) Unsafe.SizeOf<RectangleF>(),
    ];

    private static readonly uint[] InputOffsets = [0u];

    private ID3D11PixelShader* _pPixelShader;
    private ID3D11VertexShader* _pVertexShader;
    private ID3D11InputLayout* _pInputLayout;
    private readonly ID3D11Buffer*[] _pInputBuffers = new ID3D11Buffer*[1];
    private ID3D11Buffer* _pIndexBuffer;

    public DirectXTexRendererShader(ID3D11Device* pDevice)
    {
        try {
            var bytecode = this.GetType().CompileShaderFromAssemblyResource("ps_4_0", "main_ps");
            fixed (ID3D11PixelShader** ppPixelShader = &this._pPixelShader)
            fixed (void* pBytecode = bytecode)
                pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, ppPixelShader).Ensure();

            bytecode = this.GetType().CompileShaderFromAssemblyResource("vs_4_0", "main_vs");
            fixed (byte* pszPosition = "POSITION"u8)
            fixed (byte* pszTexCoord = "TEXCOORD"u8)
            fixed (ID3D11VertexShader** ppVertexShader = &this._pVertexShader)
            fixed (void* pBytecode = bytecode) {
                pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, ppVertexShader).Ensure();

                const int numDesc = 2;
                var desc = stackalloc D3D11_INPUT_ELEMENT_DESC[numDesc];
                desc[0].SetVertex(pszPosition, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT);
                desc[1].SetVertex(pszTexCoord, 0, DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT);
                fixed (ID3D11InputLayout** ppInputLayout = &this._pInputLayout) {
                    pDevice->CreateInputLayout(
                        desc,
                        numDesc,
                        pBytecode,
                        (nuint) bytecode.Length,
                        ppInputLayout).Ensure();
                }
            }

            fixed (void* pVertices = VerticesAndTexCoords)
            fixed (ID3D11Buffer** ppBuffer = &this._pInputBuffers[0]) {
                var bufferDesc = new D3D11_BUFFER_DESC(
                    (uint) Buffer.ByteLength(VerticesAndTexCoords),
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER);
                var subresourceData = new D3D11_SUBRESOURCE_DATA { pSysMem = pVertices };
                pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer).Ensure();
            }

            fixed (void* pIndices = Indices)
            fixed (ID3D11Buffer** ppBuffer = &this._pIndexBuffer) {
                var bufferDesc = new D3D11_BUFFER_DESC(
                    (uint) Buffer.ByteLength(Indices),
                    (uint) D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER);
                var subresourceData = new D3D11_SUBRESOURCE_DATA { pSysMem = pIndices };
                pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer).Ensure();
            }
        } catch (Exception) {
            this.Dispose();
            throw;
        }
    }

    ~DirectXTexRendererShader() => this.ReleaseUnmanagedResources();

    private void ReleaseUnmanagedResources()
    {
        SafeRelease(ref this._pPixelShader);
        SafeRelease(ref this._pVertexShader);
        SafeRelease(ref this._pInputLayout);
        for (var i = 0; i < this._pInputBuffers.Length; i++)
            SafeRelease(ref this._pInputBuffers[i]);
        SafeRelease(ref this._pIndexBuffer);
    }

    private void DisposePrivate(bool disposing)
    {
        _ = disposing;
        this.ReleaseUnmanagedResources();
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public void Draw(
        ID3D11DeviceContext* pDeviceContext,
        ID3D11ShaderResourceView* pShaderResourceView,
        ID3D11SamplerState* pSampler,
        ConstantBufferResource<Cbuffer> cbuffer)
    {
        fixed (ID3D11Buffer** ppBuffers = this._pInputBuffers)
        fixed (uint* pStrides = InputStrides)
        fixed (uint* pOffsets = InputOffsets)
            pDeviceContext->IASetVertexBuffers(0u, (uint) this._pInputBuffers.Length, ppBuffers, pStrides, pOffsets);
        pDeviceContext->IASetInputLayout(this._pInputLayout);
        pDeviceContext->IASetIndexBuffer(this._pIndexBuffer, DXGI_FORMAT.DXGI_FORMAT_R16_UINT, 0);
        pDeviceContext->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        pDeviceContext->VSSetShader(this._pVertexShader, null, 0);
        var buf = cbuffer.Buffer;
        pDeviceContext->VSSetConstantBuffers(0, 1, &buf);

        pDeviceContext->PSSetShader(this._pPixelShader, null, 0);
        pDeviceContext->PSSetShaderResources(0, 1, &pShaderResourceView);
        pDeviceContext->PSSetSamplers(0, 1, &pSampler);
        pDeviceContext->PSSetConstantBuffers(0, 1, &buf);

        pDeviceContext->DrawIndexed((uint) Indices.Length, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Cbuffer {
        public float Rotation;
        public float TransparencyCellSize;
        public PointF Pan;
        public SizeF EffectiveSize;
        public SizeF ClientSize;
        public RectangleF CellRectScale;
        public DXGI_RGBA TransparencyCellColor1;
        public DXGI_RGBA TransparencyCellColor2;
        public DXGI_RGBA PixelGridColor;
        public SizeF CellSourceSize;
        public VisibleColorChannelTypes ChannelFilter;
        public bool UseAlphaChannel;
    }

    public enum VisibleColorChannelTypes {
        All,
        Red,
        Green,
        Blue,
        Alpha,
    }
}
