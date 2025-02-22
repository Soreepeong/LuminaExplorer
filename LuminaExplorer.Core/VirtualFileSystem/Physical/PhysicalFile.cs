using System;
using System.IO;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed class PhysicalFile : IEquatable<PhysicalFile>, IVirtualFile {
    public PhysicalFile(FileInfo fileInfo)
    {
        this.FileInfo = fileInfo;
    }

    public FileInfo FileInfo { get; }

    public IVirtualFolder Parent =>
        this.FileInfo.Directory is { } d ? new PhysicalFolder(d) : MyComputerFolder.Instance;

    public uint? NameHash => null;

    public string Name => this.FileInfo.Name;

    public bool NameResolved => true;

    public override string ToString() => this.FileInfo.Name;

    public bool Equals(PhysicalFile? other) => Equals(this.FileInfo, other?.FileInfo);

    public bool Equals(IVirtualFile? other) => Equals(this.FileInfo, (other as PhysicalFile)?.FileInfo);

    public override bool Equals(object? obj) => Equals(this.FileInfo, (obj as PhysicalFile)?.FileInfo);

    public override int GetHashCode() => this.FileInfo.GetHashCode();
}
