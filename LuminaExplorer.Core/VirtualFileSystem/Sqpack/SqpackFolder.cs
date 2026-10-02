using System;
using System.Collections.Generic;
using System.Text;
using Lumina.Misc;

namespace LuminaExplorer.Core.VirtualFileSystem.Sqpack;

public class SqpackFolder : IEquatable<SqpackFolder>, IVirtualFolder {
    public const string NotNormalSuffix = "\0";
    public const string UnknownContainerName = "<unknown>" + NotNormalSuffix;

    internal readonly Dictionary<string, SqpackFolder> Folders = new();
    internal readonly List<SqpackFile> Files = new();

    private SqpackFolder(string name, uint hash, SqpackFolder? parent)
    {
        this.ParentTyped = parent;
        this.Name = $"{name}/";
        this.PathHash = hash;
    }

    public SqpackFolder? ParentTyped { get; internal set; }

    public Exception? AccessException => null;

    public IVirtualFolder? Parent => this.ParentTyped;

    public uint? PathHash { get; }

    public string Name { get; internal set; }

    public void Refresh()
    { }

    public bool FileNamesResolveAttempted { get; internal set; }

    public bool IsUnknownContainer => this.Name == UnknownContainerName + "/";

    public bool IsUnknownFolder => this.Name.StartsWith("~") && this.Name.EndsWith(NotNormalSuffix);

    public bool Equals(SqpackFolder? other) =>
        this.PathHash!.Value == other?.PathHash!.Value &&
        0 == string.Compare(this.Name, other?.Name, StringComparison.InvariantCultureIgnoreCase) &&
        Equals(this.Parent, other?.Parent);

    public bool Equals(IVirtualFolder? other) => this.Equals(other as SqpackFolder);

    public override bool Equals(object? obj) => this.Equals(obj as SqpackFolder);

    public override int GetHashCode() => (int) this.PathHash!.Value;

    public override string ToString() => this.Name;

    internal static SqpackFolder CreateRoot() => new("", Crc32.Get(Array.Empty<byte>()), null);

    internal static SqpackFolder CreateKnownEntry(string name, string fullPath, SqpackFolder parent)
        => new(name, Crc32.Get(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant().Trim('/'))), parent);

    internal static SqpackFolder CreateUnknownContainer(SqpackFolder parent)
        => new(UnknownContainerName, 0, parent);

    internal static SqpackFolder CreateUnknownEntry(int chunk, uint hash, SqpackFolder parent)
        => new($"~{chunk:X02}~{hash:X08}{NotNormalSuffix}", hash, parent);
}
