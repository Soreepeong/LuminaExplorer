using System;
using System.IO;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed class PhysicalFile : IEquatable<PhysicalFile>, IVirtualFile {
    public PhysicalFile(FileInfo fileInfo) {
        FileInfo = fileInfo;
    }

    public FileInfo FileInfo { get; }

    public IVirtualFolder Parent => FileInfo.Directory is { } d ? new PhysicalFolder(d) : MyComputerFolder.Instance;

    public uint? NameHash => null;

    public string Name => FileInfo.Name;

    public bool NameResolved => true;

    public override string ToString() => FileInfo.Name;

    public bool Equals(PhysicalFile? other) => Equals(FileInfo, other?.FileInfo);

    public bool Equals(IVirtualFile? other) => Equals(FileInfo, (other as PhysicalFile)?.FileInfo);

    public override bool Equals(object? obj) => Equals(FileInfo, (obj as PhysicalFile)?.FileInfo);

    public override int GetHashCode() => FileInfo.GetHashCode();
}