using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed class MyComputerFolder : BasePhysicalFolder {
    public static readonly MyComputerFolder Instance = new();

    private MyComputerFolder() { }

    public override bool Equals(IVirtualFolder? other) => other is MyComputerFolder;

    public override IVirtualFolder? Parent => null;

    public override string Name => "/";

    protected override List<PhysicalFolder> ResolveFolders() =>
        DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => new PhysicalFolder(x.RootDirectory)).ToList();

    protected override List<PhysicalFile> ResolveFiles() => new();
}
