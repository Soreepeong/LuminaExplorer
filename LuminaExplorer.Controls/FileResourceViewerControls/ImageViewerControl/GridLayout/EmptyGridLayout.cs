using System.Drawing;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

public sealed class EmptyGridLayout : IGridLayout {
    public static readonly EmptyGridLayout Instance = new();

    private EmptyGridLayout() { }

    public Size GridSize => Size.Empty;
    
    public Rectangle RectOf(int cellIndex) => Rectangle.Empty;
}
