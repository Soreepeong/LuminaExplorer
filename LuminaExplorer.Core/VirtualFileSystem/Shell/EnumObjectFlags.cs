using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[Flags]
public enum EnumObjectFlags {
    /// <summary>Windows 7 and later. The calling application is checking for the existence of child items in the folder.</summary>
    CheckingForChildren = 0x10,

    /// <summary>Include items that are folders in the enumeration.</summary>
    Folders = 0x20,

    /// <summary>Include items that are not folders in the enumeration.</summary>
    NonFolders = 0x40,

    /// <summary>Include hidden items in the enumeration. This does not include hidden system items. (To include hidden system items, use SHCONTF_INCLUDESUPERHIDDEN.)</summary>
    IncludeHidden = 0x80,

    /// <summary>No longer used; always assumed. IShellFolder::EnumObjects can return without validating the enumeration object. Validation can be postponed until the first call to IEnumnint::Next. Use this flag when a user interface might be displayed prior to the first IEnumnint::Next call. For a user interface to be presented, hwnd must be set to a valid window handle.</summary>
    InitOnFirstNext = 0x100,

    /// <summary>The calling application is looking for printer objects.</summary>
    NetPrinterSearch = 0x200,

    /// <summary>The calling application is looking for resources that can be shared.</summary>
    Shareable = 0x400,

    /// <summary>Include items with accessible storage and their ancestors, including hidden items.</summary>
    Storage = 0x800,

    /// <summary>Windows 7 and later. Child folders should provide a navigation enumeration.</summary>
    NavigationEnum = 0x1000,

    /// <summary>Windows Vista and later. The calling application is looking for resources that can be enumerated quickly.</summary>
    FastItems = 0x2000,

    /// <summary>Windows Vista and later. Enumerate items as a simple list even if the folder itself is not structured in that way.</summary>
    FlatList = 0x4000,

    /// <summary>Windows Vista and later. The calling application is monitoring for change notifications. This means that the enumerator does not have to return all results. Items can be reported through change notifications.</summary>
    EnableAsync = 0x8000,

    /// <summary>Windows 7 and later. Include hidden system items in the enumeration. This value does not include hidden non-system items. (To include hidden non-system items, use SHCONTF_INCLUDEHIDDEN.)</summary>
    IncludeSuperHidden = 0x10000,
}