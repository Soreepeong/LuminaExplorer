using System.Diagnostics;
using Lumina.Misc;

namespace LuminaExplorer.Core.LazySqPackTree;

public class VirtualFolder {
    public const string UpFolderKey = "../";
    public const string NotNormalSuffix = "\0";
    public const string UnknownContainerName = "<unknown>" + NotNormalSuffix;
    public readonly uint FolderHash;
    public readonly Dictionary<string, VirtualFolder> Folders = new();
    public readonly List<VirtualFile> Files = new();

    public bool FileNamesResolveAttempted { get; internal set; }

    private VirtualFolder(string name, uint hash, VirtualFolder? parent) {
        Parent = parent;
        Name = $"{name}/";
        FolderHash = hash;
        if (parent is not null)
            Folders.Add(UpFolderKey, parent);
    }

    private VirtualFolder(string name, VirtualFolder? parent)
        : this(
            name,
            Crc32.Get(
                parent is null
                    ? name.ToLowerInvariant()
                    : $"{parent.FullPath}{name}".Trim('/').ToLowerInvariant()),
            parent) { }

    internal VirtualFolder GetOrCreateSubfolder(string path) {
        var sepOffset = path.IndexOf('/');
        var name = sepOffset == -1 ? path : path[..sepOffset];
        if (!Folders.TryGetValue(name, out var subfolder))
            Folders.Add(name, subfolder = new(name, this));
        if (sepOffset != -1)
            subfolder = subfolder.GetOrCreateSubfolder(path[(sepOffset + 1)..]);
        return subfolder;
    }

    internal bool TryResolve(string? fullName) {
        if (!IsUnknownFolder || fullName is null)
            return false;
        
        fullName = fullName.Trim('/');
        if (Crc32.Get(fullName.ToLowerInvariant()) != FolderHash)
            return false;
        
        Debug.Assert(Parent?.IsUnknownContainer is true, "I should be in an <unknown> folder.");
        Debug.Assert(Parent.Parent is not null, "<unknown> folder must have a parent.");

        var chunkRootName = Parent.Parent.FullPath.Trim('/');
        if (!fullName.StartsWith(chunkRootName))
            return false;
        
        var subPath = fullName[(chunkRootName.Length + 1)..];
        var tempDir = GetOrCreateSubfolder(subPath);
        Debug.Assert(!tempDir.Files.Any(), "Temporary folder must be empty.");
        Debug.Assert(tempDir.Folders.All(x => x.Key == UpFolderKey), "Temporary folder must be empty.");
        Debug.Assert(tempDir.Parent is not null, "Temporary folder must have a parent.");
        tempDir.Parent.Folders.Remove(tempDir.Name);
        tempDir.Parent.Folders.Add(tempDir.Name, this);
        Name = tempDir.Name;
        Parent = tempDir.Parent;
        Parent.Folders.Remove(Name);
        return true;
    }

    public string Name { get; private set; }

    public bool IsUnknownContainer => Name == UnknownContainerName;

    public bool IsUnknownFolder => Name.StartsWith("~") && Name.EndsWith(NotNormalSuffix);
    
    public VirtualFolder? Parent { get; private set; }

    public string FullPath => $"{Parent?.FullPath ?? ""}{Name}";

    public override string ToString() => Name;

    internal static VirtualFolder CreateRoot() => new("", Crc32.Get(Array.Empty<byte>()), null);

    internal static VirtualFolder CreateUnknownContainer(VirtualFolder? parent)
        => new(UnknownContainerName, 0, parent);
    
    internal static VirtualFolder CreateUnknownEntry(int chunk, uint hash, VirtualFolder? parent)
        => new($"~{chunk:X02}~{hash:X08}{NotNormalSuffix}", hash, parent);
}
