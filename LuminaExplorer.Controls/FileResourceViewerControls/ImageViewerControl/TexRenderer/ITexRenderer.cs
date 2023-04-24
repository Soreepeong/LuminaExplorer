using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;

internal interface ITexRenderer : IDisposable {
    event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;
    
    Exception? LastException { get; }

    RectangleF? AutoDescriptionRectangle { get; set; }

    public void UiThreadInitialize();

    bool Draw(PaintEventArgs e);

    bool UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current);
    
    bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask);
    
    bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask);
}