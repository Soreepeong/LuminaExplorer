using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

[Flags]
public enum ShellItemFlags : uint {
    /// <summary>The specified items can be copied.</summary>
    CanCopy = 0x00000001,

    /// <summary>The specified items can be moved.</summary>
    CanMove = 0x00000002,

    /// <summary>Shortcuts can be created for the specified items. This attribute has the same value as DROPEFFECT_LINK.</summary>
    /// <remarks>
    /// If a namespace extension returns this attribute, a Create Shortcut entry with a default handler is added to the shortcut menu that is displayed during drag-and-drop operations. The extension can also implement its own handler for the link verb in place of the default. If the extension does so, it is responsible for creating the shortcut.
    /// A Create Shortcut item is also added to the Windows Explorer File menu and to normal shortcut menus.
    /// If the item is selected, your application's IContextMenu::InvokeCommand method is invoked with the lpVerb member of the CMINVOKECOMMANDINFO structure set to link. Your application is responsible for creating the link.
    /// </remarks>
    CanLink = 0x00000004,

    /// <summary>The specified items can be bound to an IStorage object through IShellFolder::BindToObject. For more information about namespace manipulation capabilities, see IStorage.</summary>
    Storage = 0x00000008,

    /// <summary>The specified items can be renamed. Note that this value is essentially a suggestion; not all namespace clients allow items to be renamed. However, those that do must have this attribute set.</summary>
    CanRename = 0x00000010,

    /// <summary>The specified items can be deleted.</summary>
    CanDelete = 0x00000020,

    /// <summary>The specified items have property sheets.</summary>
    HasPropSheet = 0x00000040,

    /// <summary>The specified items are drop targets.</summary>
    DropTarget = 0x00000100,

    /// <summary>This flag is a mask for the capability attributes: SFGAO_CANCOPY, SFGAO_CANMOVE, SFGAO_CANLINK, SFGAO_CANRENAME, SFGAO_CANDELETE, SFGAO_HASPROPSHEET, and SFGAO_DROPTARGET. Callers normally do not use this value.</summary>
    CapAbilityMask = 0x00000177,

    /// <summary>Windows 7 and later. The specified items are system items.</summary>
    System = 0x00001000,

    /// <summary>The specified items are encrypted and might require special presentation.</summary>
    Encrypted = 0x00002000,

    /// <summary>Accessing the item (through IStream or other storage interfaces) is expected to be a slow operation. Applications should avoid accessing items flagged with SFGAO_ISSLOW.</summary>
    /// <remarks>Opening a stream for an item is generally a slow operation at all times. SFGAO_ISSLOW indicates that it is expected to be especially slow, for example in the case of slow network connections or offline (FILE_ATTRIBUTE_OFFLINE) files. However, querying SFGAO_ISSLOW is itself a slow operation. Applications should query SFGAO_ISSLOW only on a background thread. An alternate method, such as retrieving the PKEY_FileAttributes property and testing for FILE_ATTRIBUTE_OFFLINE, could be used in place of a method call that involves SFGAO_ISSLOW.</remarks>
    IsSlow = 0x00004000,

    /// <summary>The specified items are shown as dimmed and unavailable to the user.</summary>
    Ghosted = 0x00008000,

    /// <summary>The specified items are shortcuts.</summary>
    Link = 0x00010000,

    /// <summary>The specified objects are shared.</summary>
    Share = 0x00020000,

    /// <summary>The specified items are read-only. In the case of folders, this means that new items cannot be created in those folders. This should not be confused with the behavior specified by the FILE_ATTRIBUTE_READONLY flag retrieved by IColumnProvider::GetItemData in a SHCOLUMNDATA structure. FILE_ATTRIBUTE_READONLY has no meaning for Win32 file system folders.</summary>
    ReadOnly = 0x00040000,

    /// <summary>The item is hidden and should not be displayed unless the Show hidden files and folders option is enabled in Folder Settings.</summary>
    Hidden = 0x00080000,

    /// <summary>Do not use.</summary>
    DisplayAttrMask = 0x000FC000,

    /// <summary>The items are nonenumerated items and should be hidden. They are not returned through an enumerator such as that created by the IShellFolder::EnumObjects method.</summary>
    NonEnumerated = 0x00100000,

    /// <summary>The items contain new content, as defined by the particular application.</summary>
    NewContent = 0x00200000,

    /// <summary>Indicates that the item has a stream associated with it. That stream can be accessed through a call to IShellFolder::BindToObject or IShellItem::BindToHandler with IID_IStream in the riid parameter.</summary>
    Stream = 0x00400000,

    /// <summary>Children of this item are accessible through IStream or IStorage. Those children are flagged with SFGAO_STORAGE or SFGAO_STREAM.</summary>
    StorageAncestor = 0x00800000,

    /// <summary>When specified as input, SFGAO_VALIDATE instructs the folder to validate that the items contained in a folder or Shell item array exist. If one or more of those items do not exist, IShellFolder::GetAttributesOf and IShellItemArray::GetAttributes return a failure code. This flag is never returned as an [out] value. When used with the file system folder, SFGAO_VALIDATE instructs the folder to discard cached properties retrieved by clients of IShellFolder2::GetDetailsEx that might have accumulated for the specified items.</summary>
    Validate = 0x01000000,

    /// <summary>The specified items are on removable media or are themselves removable devices.</summary>
    Removable = 0x02000000,

    /// <summary>The specified items are compressed.</summary>
    Compressed = 0x04000000,

    /// <summary>The specified items can be hosted inside a web browser or Windows Explorer frame.</summary>
    Browsable = 0x08000000,

    /// <summary>The specified folders are either file system folders or contain at least one descendant (child, grandchild, or later) that is a file system (SFGAO_FILESYSTEM) folder.</summary>
    FileSysAncestor = 0x10000000,

    /// <summary>The specified items are folders. Some items can be flagged with both SFGAO_STREAM and SFGAO_FOLDER, such as a compressed file with a .zip file name extension. Some applications might include this flag when testing for items that are both files and containers.</summary>
    Folder = 0x20000000,

    /// <summary>The specified folders or files are part of the file system (that is, they are files, directories, or root directories). The parsed names of the items can be assumed to be valid Win32 file system paths. These paths can be either UNC or drive-letter based.</summary>
    FileSystem = 0x40000000,

    /// <summary>This flag is a mask for the storage capability attributes: SFGAO_STORAGE, SFGAO_LINK, SFGAO_READONLY, SFGAO_STREAM, SFGAO_STORAGEANCESTOR, SFGAO_FILESYSANCESTOR, SFGAO_FOLDER, and SFGAO_FILESYSTEM. Callers normally do not use this value.</summary>
    StorageCapMask = 0x70C50008,

    /// <summary>The specified folders have subfolders. The SFGAO_HASSUBFOLDER attribute is only advisory and might be returned by Shell folder implementations even if they do not contain subfolders. Note, however, that the converse—failing to return SFGAO_HASSUBFOLDER—definitively states that the folder objects do not have subfolders.</summary>
    /// <remarks>Returning SFGAO_HASSUBFOLDER is recommended whenever a significant amount of time is required to determine whether any subfolders exist. For example, the Shell always returns SFGAO_HASSUBFOLDER when a folder is located on a network drive.</remarks>
    HasSubfolder = 0x80000000,

    /// <summary>This flag is a mask for content attributes, at present only SFGAO_HASSUBFOLDER. Callers normally do not use this value.</summary>
    ContentsMask = 0x80000000,

    /// <summary>Mask used by the PKEY_SFGAOFlags property to determine attributes that are considered to cause slow calculations or lack context: SFGAO_ISSLOW, SFGAO_READONLY, SFGAO_HASSUBFOLDER, and SFGAO_VALIDATE. Callers normally do not use this value.</summary>
    PkeysMask = 0x81044000,
}