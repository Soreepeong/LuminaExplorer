using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed class PhysicalFolder : BasePhysicalFolder, IEquatable<PhysicalFolder> {
    public PhysicalFolder(DirectoryInfo directoryInfo)
    {
        this.DirectoryInfo = directoryInfo;
    }

    public DirectoryInfo DirectoryInfo { get; }

    public override IVirtualFolder Parent =>
        this.DirectoryInfo.Parent is { } p ? new PhysicalFolder(p) : MyComputerFolder.Instance;

    public override string Name => this.DirectoryInfo.Name.TrimEnd('\\', '/') + "/";

    protected override List<PhysicalFolder> ResolveFolders() =>
        this.DirectoryInfo.EnumerateDirectories().Select(x => new PhysicalFolder(x)).ToList();

    protected override List<PhysicalFile> ResolveFiles() =>
        this.DirectoryInfo.EnumerateFiles().Select(x => new PhysicalFile(x)).ToList();

    public override string ToString() => this.DirectoryInfo.Name;

    public bool Equals(PhysicalFolder? other) => Equals(this.DirectoryInfo, other?.DirectoryInfo);

    public override bool Equals(IVirtualFolder? other) =>
        Equals(this.DirectoryInfo, (other as PhysicalFolder)?.DirectoryInfo);

    public override bool Equals(object? obj) => Equals(this.DirectoryInfo, (obj as PhysicalFolder)?.DirectoryInfo);

    public override int GetHashCode() => this.DirectoryInfo.GetHashCode();
}
