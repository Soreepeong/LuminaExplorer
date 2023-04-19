using Lumina.Data;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public abstract class AbstractFileResourceViewerControl<T> : Control
    where T : FileResource {
    
    public VirtualSqPackTree? Tree { get; private set; }
    
    public VirtualFile? File { get; private set; }
    
    public T? FileResource { get; private set; }

    public virtual void SetFile(VirtualSqPackTree tree, VirtualFile file, T fileResource) {
        Tree = tree;
        File = file;
        FileResource = fileResource;
    }

    public virtual void ClearFile() {
        Tree = null;
        File = null;
        FileResource = null;
    }
    
    public delegate void FileChangedEvent(object sender, VirtualFile file, T fileResource);

    public delegate void FileClearedEvent(object sender);
}