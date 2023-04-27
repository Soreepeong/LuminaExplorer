using System;
using System.Runtime.InteropServices;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

/// <summary>
/// Exposed by all Shell namespace folder objects, its methods are used to manage folders.
/// 
/// https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishellfolder
/// </summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214E6-0000-0000-C000-000000000046")]
public interface IShellFolder {
    /// <summary>Translates a file object's or folder's display name into an item identifier list.</summary>
    /// <param name="hwnd">Optional window handle</param>
    /// <param name="pbc">Optional bind context that controls the parsing operation. This parameter is normally set to NULL.</param>
    /// <param name="pszDisplayName">Null-terminated UNICODE string with the display name</param>
    /// <param name="pchEaten">Pointer to a ULONG value that receives the number of characters of the display name that was parsed.</param>
    /// <param name="ppidl">Pointer to an ITEMnint pointer that receives the item identifier list for the object.</param>
    /// <param name="pdwAttributes">Optional parameter that can be used to query for file attributes.this can be values from the SFGAO enum</param>
    void ParseDisplayName(nint hwnd, nint pbc, string pszDisplayName, uint pchEaten, out nint ppidl,
        uint pdwAttributes);

    /// <summary>Allows a client to determine the contents of a folder by creating an item identifier enumeration object and returning its IEnumnint interface.</summary>
    /// <param name="hwnd">If user input is required to perform the enumeration, this window handle should be used by the enumeration object as the parent window to take user input.</param>
    /// <param name="grfFlags">Flags indicating which items to include in the  enumeration. For a list of possible values, see the SHCONTF enum.</param>
    /// <param name="ppEnumnint">Address that receives a pointer to the IEnumnint interface of the enumeration object created by this method.</param>
    void EnumObjects(nint hwnd, EnumObjectFlags grfFlags, out IEnumIDList ppEnumnint);

    /// <summary>Retrieves an IShellFolder object for a subfolder.</summary>
    /// <param name="pidl">Address of an ITEMnint structure (PIDL) that identifies the subfolder.</param>
    /// <param name="pbc">Optional address of an IBindCtx interface on a bind context object to be used during this operation.</param>
    /// <param name="riid">Identifier of the interface to return.</param>
    /// <param name="ppv">Address that receives the interface pointer.</param>
    void BindToObject(nint pidl, nint pbc, [In] ref Guid riid, out nint ppv);

    /// <summary>Requests a pointer to an object's storage interface.</summary>
    /// <param name="pidl">Address of an ITEMnint structure that identifies the subfolder relative to its parent folder.</param>
    /// <param name="pbc">Optional address of an IBindCtx interface on a bind context object to be  used during this operation.</param>
    /// <param name="riid">Interface identifier (IID) of the requested storage interface.</param>
    /// <param name="ppv">Address that receives the interface pointer specified by riid.</param>
    void BindToStorage(nint pidl, nint pbc, [In] ref Guid riid, out nint ppv);

    /// <summary>Determines the relative order of two file objects or folders, given their item identifier lists.</summary>
    /// <returns>
    /// If this method is successful, the CODE field of the HRESULT contains one of the following values
    /// (the code can be retrived using the helper function GetHResultCode):
    /// Negative: A negative return value indicates that the first item should precede the second (pidl1 &lt; pidl2).
    /// Positive: A positive return value indicates that the first item should follow the second (pidl1 &gt; pidl2).
    /// Zero: A return value of zero indicates that the two items are the same (pidl1 = pidl2).
    /// </returns>
    /// <param name="lParam">
    /// Value that specifies how the comparison should be performed.
    /// The lower sixteen bits of lParam define the sorting rule.
    /// The upper sixteen bits of lParam are used for flags that modify the sorting rule.
    /// Values can be from the SHCIDS enum.
    /// </param>
    /// <param name="pidl1">Pointer to the first item's ITEMnint structure.</param>
    /// <param name="pidl2">Pointer to the second item's ITEMnint structure.</param>
    /// <returns></returns>
    [PreserveSig]
    int CompareIDs(int lParam, nint pidl1, nint pidl2);

    /// <summary>Requests an object that can be used to obtain information from or interact with a folder object.</summary>
    /// <returns>If this method succeeds, it returns S_OK. Otherwise, it returns an HRESULT error code.</returns>
    /// <param name="hwndOwner">Handle to the owner window.</param>
    /// <param name="riid">Identifier of the requested interface.</param>
    /// <param name="ppv">Address of a pointer to the requested interface.</param>
    void CreateViewObject(nint hwndOwner, [In] ref Guid riid, out nint ppv);

    /// <summary>Retrieves the attributes of one or more file objects or subfolders.</summary>
    /// <param name="cidl">Number of file objects from which to retrieve attributes.</param>
    /// <param name="apidl">Address of an array of pointers to ITEMnint structures, each of which  uniquely identifies a file object relative to the parent folder.</param>
    /// <param name="rgfInOut">Address of a single ULONG value that, on entry contains the attributes that the caller is
    /// requesting. On exit, this value contains the requested attributes that are common to all of the specified objects. this value can be from the SFGAO enum
    /// </param>
    void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] apidl,
        ref ShellItemFlags rgfInOut);

    /// <summary>Retrieves an OLE interface that can be used to carry out actions on the specified file objects or folders.</summary>
    /// <param name="hwndOwner">Handle to the owner window that the client should specify if it displays a dialog box or message box.</param>
    /// <param name="cidl">Number of file objects or subfolders specified in the apidl parameter.</param>
    /// <param name="apidl">Address of an array of pointers to ITEMnint  structures, each of which  uniquely identifies a file object or subfolder relative to the parent folder.</param>
    /// <param name="riid">Identifier of the COM interface object to return.</param>
    /// <param name="rgfReserved">Reserved.</param>
    /// <param name="ppv">Pointer to the requested interface.</param>
    void GetUIObjectOf(nint hwndOwner, uint cidl,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
        nint[] apidl, [In] ref Guid riid, uint rgfReserved,
        out nint ppv);

    /// <summary>
    /// Retrieves the display name for the specified file object or subfolder.
    /// Return value: error code, if any
    /// </summary>
    /// <param name="pidl">Address of an ITEMnint structure (PIDL)  that uniquely identifies the file  object or subfolder relative to the parent  folder.</param>
    /// <param name="uFlags">Flags used to request the type of display name to return. For a list of possible values.</param>
    /// <param name="pName">Address of a STRRET structure in which to return the display name.</param>
    void GetDisplayNameOf(nint pidl, NameFlags uFlags, out StrRet pName);

    /// <summary>
    /// Sets the display name of a file object or subfolder, changing the item
    /// identifier in the process.
    /// Return value: error code, if any
    /// </summary>
    /// <param name="hwnd">Handle to the owner window of any dialog or message boxes that the client displays.</param>
    /// <param name="pidl">Pointer to an ITEMnint structure that uniquely identifies the file object or subfolder relative to the parent folder.</param>
    /// <param name="pszName">Pointer to a null-terminated string that specifies the new display name.</param>
    /// <param name="uFlags">Flags indicating the type of name specified by  the lpszName parameter. For a list of possible values, see the description of the SHGNO enum.</param>
    /// <param name="ppidlOut"></param>
    void SetNameOf(nint hwnd, nint pidl, string pszName, NameFlags uFlags, out nint ppidlOut);

}