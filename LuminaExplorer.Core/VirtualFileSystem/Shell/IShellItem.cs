using System;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
public interface IShellItem {
    void BindToHandler(IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)]Guid bhid,
        [MarshalAs(UnmanagedType.LPStruct)]Guid riid,
        out IntPtr ppv);

    void GetParent(out IShellItem ppsi);

    void GetDisplayName(SIGDN sigdnName,[MarshalAs(UnmanagedType.LPWStr)] out string pszName);

    void GetAttributes(ShellItemFlags sfgaoMask, out ShellItemFlags psfgaoAttribs);

    void Compare(IShellItem psi, uint hint, out int piOrder);
};