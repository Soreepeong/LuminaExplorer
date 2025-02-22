using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.Util;

public static class FieldExtensions {
    public static bool TryGetCopyOfFixedArray(this FieldInfo info, object owner, out Array array)
    {
        if (info.GetCustomAttributes(typeof(FixedBufferAttribute), false)
                .FirstOrDefault() is not FixedBufferAttribute fixedAttr) {
            array = null!;
            return false;
        }

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
        array = Array.CreateInstance(fixedAttr.ElementType, fixedAttr.Length);
        unsafe {
            var numBytes = Marshal.SizeOf(fixedAttr.ElementType) * fixedAttr.Length;
            var sourceRef = TypedReference.MakeTypedReference(owner, [info]);
            fixed (void* target = &MemoryMarshal.GetArrayDataReference(array))
                Buffer.MemoryCopy(*(void**) &sourceRef, target, numBytes, numBytes);
        }
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

        return true;
    }
}
