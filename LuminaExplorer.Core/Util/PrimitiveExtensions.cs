using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LuminaExplorer.Core.Util; 

public static class PrimitiveExtensions {
    public static bool IsWhiteSpace(this uint n) => n < 0x10000 && char.IsWhiteSpace((char) n);
    
    public static string ExtractCString(this Span<byte> s, Encoding? encoding = null) {
        encoding ??= Encoding.UTF8;
        var i = s.IndexOf((byte)0);
        return encoding.GetString(i == -1 ? s : s[..i]);
    }

    public static Matrix4x4 Normalize(this Matrix4x4 val) => Matrix4x4.Multiply(val, 1f / val.M44);
    
    public static Vector3 NormalizeNormal(this Vector3 val) =>
        Vector3.Zero == val ? Vector3.UnitX : Vector3.Normalize(val);

    public static Vector3 NormalizePosition(this Vector4 val) => new(val.X, val.Y, val.Z);
    
    public static Vector2 NormalizeUv(this Vector4 val) => new(val.X, val.Y);

    public static Vector4 NormalizeTangent(this Vector4 val) {
        var normXyz = Vector3.Normalize(new(val.X, val.Y, val.Z));
        // Tangent W should be 1 or -1, but sometimes XIV has their -1 as 0?
        var w = val.W == 0 ? -1 : val.W;
        return new(normXyz.X, normXyz.Y, normXyz.Z, w);
    }

    public static List<float>? ToFloatList(this Vector3 val, Vector3 defaultValue, float threshold) {
        if (Math.Abs(val.X - defaultValue.X) < threshold &&
            Math.Abs(val.Y - defaultValue.Y) < threshold &&
            Math.Abs(val.Z - defaultValue.Z) < threshold)
            return null;
        return new() {val.X, val.Y, val.Z};
    }

    public static List<float>? ToFloatList(this Quaternion val, Quaternion defaultValue, float threshold) {
        if (Math.Abs(val.X - defaultValue.X) < threshold &&
            Math.Abs(val.Y - defaultValue.Y) < threshold &&
            Math.Abs(val.Z - defaultValue.Z) < threshold &&
            Math.Abs(val.W - defaultValue.W) < threshold)
            return null;
        return new() {val.X, val.Y, val.Z, val.W};
    }
}
