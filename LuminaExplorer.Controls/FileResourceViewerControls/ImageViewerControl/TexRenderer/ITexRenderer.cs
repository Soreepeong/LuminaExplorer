using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.TexRenderer;

internal interface ITexRenderer : IDisposable {
    event Action<Task<IBitmapSource>>? AnyBitmapSourceSliceAvailableForDrawing;
    
    Exception? LastException { get; }

    public void UiThreadInitialize();

    bool Draw(PaintEventArgs e);

    void UpdateBitmapSource(Task<IBitmapSource>? previous, Task<IBitmapSource>? current);
    
    bool IsAnyVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask);
    
    bool IsEveryVisibleSliceReadyForDrawing(Task<IBitmapSource>? bitmapSourceTask);
}