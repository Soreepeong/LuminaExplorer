namespace LuminaExplorer.Core.LazySqPackTree;

public sealed partial class VirtualSqPackTree {
    public Task Search(
        string query,
        Action<VirtualFolder> folderFoundCallback,
        Action<VirtualFile> fileFoundCallback,
        CancellationToken cancellationToken = default) {
        throw new NotImplementedException();
    }
}
