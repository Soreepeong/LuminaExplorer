using Lumina.Data;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public abstract class AbstractFileResourceViewerControl<T> : AbstractFileResourceViewerControl
    where T : FileResource {

    public T? FileResourceTyped { get; private set; }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        FileResourceTyped = fileResource as T;
        base.SetFile(tree, file, fileResource);
    }

    public override void ClearFile() {
        FileResourceTyped = null;
        base.ClearFile();
    }
}
