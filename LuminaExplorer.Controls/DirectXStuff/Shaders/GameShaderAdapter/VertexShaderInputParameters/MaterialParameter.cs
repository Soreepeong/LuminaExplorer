using System;
using System.Numerics;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters; 

[StructLayout(LayoutKind.Explicit, Size = 0xB0)]
[InputId(InputId.MaterialParameter)]
public unsafe struct MaterialParameter {
    public const int ValueCount = 11;
    
    [FieldOffset(0)] public fixed float Values[ValueCount * 4];

    public Vector4 this[int i] {
        get {
            if (i is < 0 or >= ValueCount)
                throw new ArgumentOutOfRangeException(nameof(i), i, null);
            var value = new Vector4();
            fixed (void* p = &Values[i * 4])
                Buffer.MemoryCopy(p, &value, 16, 16);
            return value;
        }
        set {
            if (i is < 0 or >= ValueCount)
                throw new ArgumentOutOfRangeException(nameof(i), i, null);
            fixed (void* p = &Values[i * 4])
                Buffer.MemoryCopy(&value, p, 16, 16);
        }
    }
}