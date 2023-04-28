using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public sealed unsafe class Tex2DShader : IDisposable {
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

    public static readonly uint[] InputStrides = {
        (uint) (Buffer.ByteLength(VerticesAndTexCoords) / VerticesAndTexCoords.GetLength(0)),
        (uint) Unsafe.SizeOf<RectangleF>(),
    };

    private ID3D11PixelShader* _pPixelShader;
    private ID3D11VertexShader* _pVertexShader;
    private ID3D11InputLayout* _pInputLayout;
    private ID3D11Buffer* _pIndexBuffer;

    public Tex2DShader(ID3D11Device* pDevice) {
        try {
            var bytecode = DxShaders.Tex2DPixelShader;
            fixed (ID3D11PixelShader** p2 = &_pPixelShader)
            fixed (void* pBytecode = bytecode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2));

            bytecode = DxShaders.Tex2DVertexShader;
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
            fixed (ID3D11Buffer** ppBuffer = &InputBuffers[0]) {
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

    public void Dispose() {
        SafeRelease(ref _pPixelShader);
        SafeRelease(ref _pVertexShader);
        SafeRelease(ref _pInputLayout);
        SafeRelease(ref InputBuffers[0]);
        SafeRelease(ref _pIndexBuffer);
    }

    public int Stride => Buffer.ByteLength(VerticesAndTexCoords) / VerticesAndTexCoords.GetLength(0);

    public ID3D11PixelShader* PixelShader => _pPixelShader;
    public ID3D11VertexShader* VertexShader => _pVertexShader;
    public ID3D11InputLayout* InputLayout => _pInputLayout;
    public ID3D11Buffer*[] InputBuffers { get; } = new ID3D11Buffer*[1];
    public ID3D11Buffer* IndexBuffer => _pIndexBuffer;
    public uint[] InputOffsets { get; } = new uint[1];

    public int NumIndices => Indices.Length;

    private static void ThrowH(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    private static void SafeRelease<T>(ref T* u) where T : unmanaged {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
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
        public VisibleColorChannelTypes ChannelFilter; // todo
        public bool DisableAlphaChannel; // todo
    }

    public enum VisibleColorChannelTypes {
        All,
        Red,
        Green,
        Blue,
        Alpha,
    }
}
