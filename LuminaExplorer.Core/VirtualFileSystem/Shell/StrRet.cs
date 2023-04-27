using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[StructLayout(LayoutKind.Explicit, Size = 264)]
public struct StrRet {
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 264)] [FieldOffset(0)]
    public byte[] Data;

    public string ToStringAndDispose() {
        var hr = StrRetToStrW(ref this, 0, out var sz);
        return hr < 0 ? Marshal.GetExceptionForHR(hr)!.ToString() : sz;
    }

    [DllImport("Shlwapi.dll")]
    private static extern int StrRetToStrW(
        ref StrRet strret,
        in nint pidl,
        [MarshalAs(UnmanagedType.LPWStr)] out string psz);
}
