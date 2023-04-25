using System;
using System.Collections.Generic;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public class VirtualFolder : IVirtualFolder {
    public const string NotNormalSuffix = "\0";
    public const string UnknownContainerName = "<unknown>" + NotNormalSuffix;
    
    internal readonly Dictionary<string, VirtualFolder> Folders = new();
    internal readonly List<VirtualFile> Files = new();

    private VirtualFolder(string name, uint hash, VirtualFolder? parent) {
        ParentTyped = parent;
        Name = $"{name}/";
        PathHash = hash;
        if (parent is not null)
            Folders.Add(IVirtualFolder.UpFolderKey, parent);
    }

    public VirtualFolder? ParentTyped { get; internal set; }

    public IVirtualFolder? Parent => ParentTyped;
    
    public uint PathHash { get; }

    public string Name { get; internal set; }

    public bool FileNamesResolveAttempted { get; internal set; }

    public bool IsUnknownContainer => Name == UnknownContainerName;

    public bool IsUnknownFolder => Name.StartsWith("~") && Name.EndsWith(NotNormalSuffix);

    public override string ToString() => Name;

    internal static VirtualFolder CreateRoot() => new("", Crc32.Get(Array.Empty<byte>()), null);

    internal static VirtualFolder CreateKnownEntry(string name, string fullPath, VirtualFolder parent)
        => new(name, Crc32.Get(fullPath.ToLowerInvariant().Trim('/')), parent);

    internal static VirtualFolder CreateUnknownContainer(VirtualFolder parent)
        => new(UnknownContainerName, 0, parent);
    
    internal static VirtualFolder CreateUnknownEntry(int chunk, uint hash, VirtualFolder parent)
        => new($"~{chunk:X02}~{hash:X08}{NotNormalSuffix}", hash, parent);
}
