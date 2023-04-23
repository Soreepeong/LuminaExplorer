using System;
using System.Collections.Generic;
using Lumina.Misc;

namespace LuminaExplorer.Core.LazySqPackTree;

public class VirtualFolder {
    public const string UpFolderKey = "../";
    public const string NotNormalSuffix = "\0";
    public const string UnknownContainerName = "<unknown>" + NotNormalSuffix;
    
    internal readonly Dictionary<string, VirtualFolder> Folders = new();
    
    public readonly uint FolderHash;
    public readonly List<VirtualFile> Files = new();

    public bool FileNamesResolveAttempted { get; internal set; }

    private VirtualFolder(string name, uint hash, VirtualFolder? parent) {
        Parent = parent;
        Name = $"{name}/";
        FolderHash = hash;
        if (parent is not null)
            Folders.Add(UpFolderKey, parent);
    }

    public VirtualFolder? Parent { get; internal set; }
    
    public string Name { get; internal set; }

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
