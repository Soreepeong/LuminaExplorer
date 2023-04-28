using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders;

public static unsafe class DxShaders {
    public static byte[] Tex2DPixelShader => CompileShader("Tex2d", "ps_4_0", "main_ps");
    public static byte[] Tex2DVertexShader => CompileShader("Tex2d", "vs_4_0", "main_vs");

    private static byte[] CompileShader(string name, string target, string entrypointName = "main") {
        byte[] buffer;
        using (var stream = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream($"{typeof(DxShaders).Namespace}.{name}.hlsl")!)
            stream.ReadExactly(buffer = new byte[stream.Length]);

        ID3D10Blob* pCode = null;
        ID3D10Blob* pErrorMsgs = null;
        try {
            fixed (void* pTarget = Encoding.UTF8.GetBytes(target))
            fixed (void* pEntrypointName = Encoding.UTF8.GetBytes(entrypointName))
            fixed (byte* pBuffer = &buffer[0]) {
                var hr = D3DCompiler.GetApi().Compile(
                    pBuffer,
                    (nuint) buffer.Length,
                    (byte*) null,
                    null,
                    null,
                    (byte*) pEntrypointName,
                    (byte*) pTarget,
                    1, // debug
                    0,
                    &pCode,
                    &pErrorMsgs);

                if (hr < 0) {
                    if (pErrorMsgs is not null)
                        throw new(Encoding.UTF8.GetString(pErrorMsgs->Buffer));
                    Marshal.ThrowExceptionForHR(hr);
                }
            }

            buffer = new byte[pCode->Buffer.Length];
            pCode->Buffer.CopyTo(new(buffer));
            return buffer;
        } finally {
            if (pCode is not null)
                pCode->Release();
            if (pErrorMsgs is not null)
                pErrorMsgs->Release();
        }
    }
}
