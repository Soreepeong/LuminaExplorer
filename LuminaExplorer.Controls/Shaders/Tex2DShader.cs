using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Controls.Shaders;

public sealed unsafe class Tex2DShader : IDisposable {
    private static readonly float[,,] VerticesAndTexCoords = {
        // { { x, y }, { u, v } }
        { { -1, -1 }, { 0, 1 } },
        { { -1, +1 }, { 0, 0 } },
        { { +1, -1 }, { 1, 1 } },
        { { +1, +1 }, { 1, 0 } },
    };
    private static readonly ushort[,] Indices = {
        { 0, 1, 2 },
        { 3, 2, 1 },
    };

    private ID3D11PixelShader* _pixelShader;
    private ID3D11VertexShader* _vertexShader;
    private ID3D11InputLayout* _inputLayoutVertexShader;
    private ID3D11Buffer* _vertexBuffer;
    private ID3D11Buffer* _indexBuffer;

    public Tex2DShader(ID3D11Device* pDevice) {
        try {
            var bytecode = DxShaders.Tex2DPixelShader;
            fixed (ID3D11PixelShader** p2 = &_pixelShader)
            fixed (void* pBytecode = bytecode)
                ThrowH(pDevice->CreatePixelShader(pBytecode, (nuint) bytecode.Length, null, p2));

            bytecode = DxShaders.Tex2DVertexShader;
            fixed (byte* pszPosition = "POSITION"u8.ToArray())
            fixed (byte* pszTexCoord = "TEXCOORD"u8.ToArray())
            fixed (ID3D11VertexShader** p2 = &_vertexShader)
            fixed (void* pBytecode = bytecode) {
                ThrowH(pDevice->CreateVertexShader(pBytecode, (nuint) bytecode.Length, null, p2));

                var desc = new InputElementDesc[] {
                    new(pszPosition, 0, Silk.NET.DXGI.Format.FormatR32G32Float, 0, 0, InputClassification.PerVertexData, 0),
                    new(pszTexCoord, 0, Silk.NET.DXGI.Format.FormatR32G32Float, 0, 8, InputClassification.PerVertexData, 0),
                };
                fixed (InputElementDesc* pDesc = desc)
                fixed (ID3D11InputLayout** ppInputLayout = &_inputLayoutVertexShader)
                    ThrowH(pDevice->CreateInputLayout(pDesc, (uint) desc.Length, pBytecode, (nuint) bytecode.Length, ppInputLayout));
            }

            fixed (void* pVertices = VerticesAndTexCoords)
            fixed (ID3D11Buffer** ppBuffer = &_vertexBuffer) {
                var bufferDesc = new BufferDesc((uint) Buffer.ByteLength(VerticesAndTexCoords), Usage.Default, (uint) BindFlag.VertexBuffer, 0, 0, 0);
                var subresourceData = new SubresourceData(pVertices, 0u, 0u);
                ThrowH(pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer));
            }

            fixed (void* pIndices = Indices)
            fixed (ID3D11Buffer** ppBuffer = &_indexBuffer) {
                var bufferDesc = new BufferDesc((uint) Buffer.ByteLength(Indices), Usage.Default, (uint) BindFlag.IndexBuffer, 0, 0, 0);
                var subresourceData = new SubresourceData(pIndices, 0u, 0u);
                ThrowH(pDevice->CreateBuffer(&bufferDesc, &subresourceData, ppBuffer));
            }

        } catch (Exception) {
            Dispose();
            throw;
        }
    }

    public void Dispose() {
        SafeRelease(ref _pixelShader);
        SafeRelease(ref _vertexShader);
        SafeRelease(ref _inputLayoutVertexShader);
        SafeRelease(ref _vertexBuffer);
        SafeRelease(ref _indexBuffer);
    }

    public int Stride => Buffer.ByteLength(VerticesAndTexCoords) / VerticesAndTexCoords.GetLength(0);

    public ID3D11PixelShader* PixelShader => _pixelShader;
    public ID3D11VertexShader* VertexShader => _vertexShader;
    public ID3D11InputLayout* InputLayoutVertexShader => _inputLayoutVertexShader;
    public ID3D11Buffer* VertexBuffer => _vertexBuffer;
    public ID3D11Buffer* IndexBuffer => _indexBuffer;

    public int NumIndices => Indices.Length;

    private static void ThrowH(int hresult) => Marshal.ThrowExceptionForHR(hresult);

    private static void SafeRelease<T>(ref T* u) where T : unmanaged {
        if (u is not null)
            ((IUnknown*) u)->Release();
        u = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Tex2DConstantBuffer {
        public float RotateM11;
        public float RotateM12;
        public float Padding1;
        public float Padding2;
        public float RotateM21;
        public float RotateM22;
        public System.Drawing.SizeF ImageSize; // 8
        public System.Drawing.PointF Pan; // 8
        public System.Drawing.SizeF ClientSize; // 8
    }
}
