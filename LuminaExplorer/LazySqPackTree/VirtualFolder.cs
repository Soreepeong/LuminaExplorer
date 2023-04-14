namespace LuminaExplorer.LazySqPackTree;

public class VirtualFolder {
    public readonly string Name;
    public readonly Dictionary<string, VirtualFolder> Folders = new();
    public readonly List<VirtualFile> Files = new();

    public bool FileNamesResolveAttempted { get; internal set; }

    internal VirtualFolder(string name, VirtualFolder? parent) {
        Name = name;
        if (parent is not null)
            Folders.Add("..", parent);
    }

    internal VirtualFolder(int chunk, uint hash, VirtualFolder? parent)
        : this($"~{chunk:X02}~{hash:X08}", parent) { }

    internal VirtualFolder GetOrCreateSubfolder(string path) {
        var sepOffset = path.IndexOf('/');
        var name = sepOffset == -1 ? path : path[..sepOffset];
        if (!Folders.TryGetValue(name, out var subfolder))
            Folders.Add(name, subfolder = new(name, this));
        if (sepOffset != -1)
            subfolder = subfolder.GetOrCreateSubfolder(path[(sepOffset + 1)..]);
        return subfolder;
    }
}
