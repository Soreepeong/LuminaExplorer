using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LuminaExplorer.Controls.DirectXStuff.Resources;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public sealed unsafe class DirectXTexRendererShader : DirectXObject {
    private static readonly float[,,] VerticesAndTexCoords = {
        // { { x, y }, { u, v } }
        {{0, 0}, {0, 0}},
        {{0, 1}, {0, 1}},
        {{1, 0}, {1, 0}},
        {{1, 1}, {1, 1}},
    };

    private static readonly ushort[,] Indices = {
        {2, 1, 0},
        {1, 2, 3},
    };

    private static readonly uint[] InputStrides = {
        (uint) (Buffer.ByteLength(VerticesAndTexCoords) / VerticesAndTexCoords.GetLength(0)),
        (uint) Unsafe.SizeOf<RectangleF>(),
    };

    private static readonly uint[] InputOffsets = {0u};

    private ID3D11PixelShader* _pPixelShader;
    private ID3D11VertexShader* _pVertexShader;
    private ID3D11InputLayout* _pInputLayout;
    private readonly ID3D11Buffer*[] _pInputBuffers = new ID3D11Buffer*[1];
    private ID3D11Buffer* _pIndexBuffer;

    public DirectXTexRendererShader(ID3D11Device* pDevice) {
        try {
            var bytecode = GetType().CompileShaderFromAssemblyResource("ps_4_0", "main_ps");
            fixed (ID3D11PixelShader** p2 = &_pPixelShader)
            fixed (void* pBytecode = bytecode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2));

            bytecode = GetType().CompileShaderFromAssemblyResource("vs_4_0", "main_vs");
            fixed (byte* pszPosition = "POSITION"u8.ToArray())
            fixed (byte* pszTexCoord = "TEXCOORD"u8.ToArray())
            fixed (ID3D11VertexShader** p2 = &_pVertexShader)
            fixed (void* pBytecode = bytecode) {
                ThrowH(pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, p2));

                var desc = new InputElementDesc[] {
                    new(pszPosition, 0, Format.FormatR32G32Float, 0, 0, InputClassification.PerVertexData, 0),
                    new(pszTexCoord, 0, Format.FormatR32G32Float, 0, 8, InputClassification.PerVertexData, 0),
                };
                fixed (InputElementDesc* pDesc = desc)
                fixed (ID3D11InputLayout** ppInputLayout = &_pInputLayout)
                    ThrowH(pDevice->CreateInputLayout(pDesc, (uint) desc.Length, pBytecode, (nuint) bytecode.Length,
                        ppInputLayout));
            }

            fixed (void* pVertices = VerticesAndTexCoords)
            fixed (ID3D11Buffer** ppBuffer = &_pInputBuffers[0]) {
                var bufferDesc = new BufferDesc((uint) Buffer.ByteLength(VerticesAndTexCoords), Usage.Default,
                    (uint) BindFlag.VertexBuffer, 0, 0, 0);
                var subresourceData = new SubresourceData(pVertices, 0u, 0u);
                ThrowH(pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer));
            }

            fixed (void* pIndices = Indices)
            fixed (ID3D11Buffer** ppBuffer = &_pIndexBuffer) {
                var bufferDesc = new BufferDesc((uint) Buffer.ByteLength(Indices), Usage.Default,
                    (uint) BindFlag.IndexBuffer, 0, 0, 0);
                var subresourceData = new SubresourceData(pIndices, 0u, 0u);
                ThrowH(pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer));
            }
        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    protected override void Dispose(bool disposing) {
        SafeRelease(ref _pPixelShader);
        SafeRelease(ref _pVertexShader);
        SafeRelease(ref _pInputLayout);
        for (var i = 0; i < _pInputBuffers.Length; i++)
            SafeRelease(ref _pInputBuffers[i]);
        SafeRelease(ref _pIndexBuffer);
        base.Dispose(disposing);
    }

    public void Draw(
        ID3D11DeviceContext* pDeviceContext,
        ID3D11ShaderResourceView* pShaderResourceView,
        ID3D11SamplerState* pSampler,
        ConstantBufferResource<Cbuffer> cbuffer) {
        fixed (ID3D11Buffer** ppBuffers = _pInputBuffers)
        fixed (uint* pStrides = InputStrides)
        fixed (uint* pOffsets = InputOffsets)
            pDeviceContext->IASetVertexBuffers(0u,
                (uint) _pInputBuffers.Length,
                ppBuffers,
                pStrides,
                pOffsets);
        pDeviceContext->IASetInputLayout(_pInputLayout);
        pDeviceContext->IASetIndexBuffer(_pIndexBuffer, Format.FormatR16Uint, 0);
        pDeviceContext->IASetPrimitiveTopology(D3DPrimitiveTopology.D3D11PrimitiveTopologyTrianglelist);

        pDeviceContext->VSSetShader(_pVertexShader, null, 0);
        pDeviceContext->VSSetConstantBuffers(0, 1, cbuffer.Buffer);

        pDeviceContext->PSSetShader(_pPixelShader, null, 0);
        pDeviceContext->PSSetShaderResources(0, 1, pShaderResourceView);
        pDeviceContext->PSSetSamplers(0, 1, pSampler);
        pDeviceContext->PSSetConstantBuffers(0, 1, cbuffer.Buffer);

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
        public D3Dcolorvalue TransparencyCellColor1;
        public D3Dcolorvalue TransparencyCellColor2;
        public D3Dcolorvalue PixelGridColor;
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
