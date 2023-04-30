using System;
using System.Runtime.InteropServices;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using Silk.NET.Maths;

namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;

[StructLayout(LayoutKind.Explicit, Size = 0xC00)]
[InputId(InputId.JointMatrixArray)]
public unsafe struct JointMatrixArray {
    public const int ValueCount = 64;

    [FieldOffset(0)] public fixed float Values[3 * 4 * ValueCount];

    public Matrix3X4<float> this[int i] {
        get {
            if (i is < 0 or >= ValueCount)
                throw new ArgumentOutOfRangeException(nameof(i), i, null);
            var value = new Matrix3X4<float>();
            fixed (void* p = &Values[i * 12])
                Buffer.MemoryCopy(p, &value, 48, 48);
            return value;
        }
        set {
            if (i is < 0 or >= ValueCount)
                throw new ArgumentOutOfRangeException(nameof(i), i, null);
            fixed (void* p = &Values[i * 12])
                Buffer.MemoryCopy(&value, p, 48, 48);
        }
    }

    public static JointMatrixArray Default {
        get {
            JointMatrixArray res;
            for (var i = 0; i < ValueCount; i++) {
                res.Values[i * 12 + 0] = 1;
                res.Values[i * 12 + 5] = 1;
                res.Values[i * 12 + 10] = 1;
            }

            return res;
        }
    }
}