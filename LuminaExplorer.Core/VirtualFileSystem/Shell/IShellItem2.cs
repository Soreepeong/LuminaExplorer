using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using DirectN;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("7E9FB0D3-919F-4307-AB2E-9B1860310C93")]
public interface IShellItem2 : IShellItem {
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime), PreserveSig]
    int GetPropertyStore(
        [In] GetPropertyStoreOptions flags,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)]
        out IPropertyStore ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyStoreWithCreateObject([In] GetPropertyStoreOptions flags,
        [In, MarshalAs(UnmanagedType.IUnknown)]
        object punkCreateObject, [In] ref Guid riid, out IntPtr ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyStoreForKeys([In] ref PropertyKey rgKeys, [In] uint cKeys,
        [In] GetPropertyStoreOptions flags, [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.IUnknown)]
        out IPropertyStore ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetPropertyDescriptionList([In] ref PropertyKey keyType, [In] ref Guid riid, out IntPtr ppv);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void Update([In, MarshalAs(UnmanagedType.Interface)] IBindCtx pbc);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetProperty([In] ref PropertyKey key, [Out] PropVariant ppropvar);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetCLSID([In] ref PropertyKey key, out Guid pclsid);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetFileTime([In] ref PropertyKey key, out FILETIME pft);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetInt32([In] ref PropertyKey key, out int pi);

    [PreserveSig]
    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetString([In] ref PropertyKey key, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUInt32([In] ref PropertyKey key, out uint pui);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetUInt64([In] ref PropertyKey key, out ulong pull);

    [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
    void GetBool([In] ref PropertyKey key, out int pf);
}