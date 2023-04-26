using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed class PhysicalFolder : BasePhysicalFolder, IEquatable<PhysicalFolder> {
    public PhysicalFolder(DirectoryInfo directoryInfo) {
        DirectoryInfo = directoryInfo;
    }

    public DirectoryInfo DirectoryInfo { get; }

    public override IVirtualFolder Parent =>
        DirectoryInfo.Parent is { } p ? new PhysicalFolder(p) : MyComputerFolder.Instance;

    public override string Name => DirectoryInfo.Name.TrimEnd('\\', '/') + "/";

    protected override List<PhysicalFolder> ResolveFolders() =>
        DirectoryInfo.EnumerateDirectories().Select(x => new PhysicalFolder(x)).ToList();

    protected override List<PhysicalFile> ResolveFiles() =>
        DirectoryInfo.EnumerateFiles().Select(x => new PhysicalFile(x)).ToList();

    public override string ToString() => DirectoryInfo.Name;

    public bool Equals(PhysicalFolder? other) => Equals(DirectoryInfo, other?.DirectoryInfo);

    public override bool Equals(IVirtualFolder? other) =>
        Equals(DirectoryInfo, (other as PhysicalFolder)?.DirectoryInfo);

    public override bool Equals(object? obj) => Equals(DirectoryInfo, (obj as PhysicalFolder)?.DirectoryInfo);

    public override int GetHashCode() => DirectoryInfo.GetHashCode();
}
