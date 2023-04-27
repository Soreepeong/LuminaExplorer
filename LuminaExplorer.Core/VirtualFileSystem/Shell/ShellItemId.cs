using System;
using System.Runtime.InteropServices;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public readonly partial struct ShellItemId : IEquatable<ShellItemId> {
    public readonly byte[] Data = Array.Empty<byte>();

    public ShellItemId(nint pidl) {
        if (pidl == 0)
            return;

        var cb = (int) (ushort) Marshal.ReadInt16(pidl);
        Data = new byte[cb];
        Marshal.Copy(pidl, Data, 0, cb);
        Marshal.FreeCoTaskMem(pidl);
    }

    public bool Equals(ShellItemId other) => new ReadOnlySpan<byte>(Data).SequenceEqual(new(other.Data));

    public override bool Equals(object? obj) => obj is ShellItemId other && Equals(other);

    public override int GetHashCode() => (int) Crc32.Get(Data);

    public static unsafe ShellItemId operator +(ShellItemId l, ShellItemId r) {
        fixed (void* pidl1 = l.Data)
        fixed (void* pidl2 = r.Data)
            return new(ILCombine((nint) pidl1, (nint) pidl2));
    }

    public static bool operator ==(ShellItemId left, ShellItemId right) => left.Equals(right);

    public static bool operator !=(ShellItemId left, ShellItemId right) => !left.Equals(right);

    [LibraryImport("Shell32.dll")]
    private static partial nint ILCombine(nint pidl1, nint pidl2);
}