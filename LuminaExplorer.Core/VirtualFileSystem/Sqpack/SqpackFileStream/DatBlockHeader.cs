using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack.SqpackFileStream;

[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
[SuppressMessage("ReSharper", "UnusedMember.Local")]
[StructLayout(LayoutKind.Sequential)]
public struct DatBlockHeader {
    public uint HeaderSize;
    public uint Version;
    public uint CompressedSize;
    public uint DecompressedSize;

    public bool IsCompressed => CompressedSize != 32000;
}
