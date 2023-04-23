using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.GridLayout;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl {
    private const float LayoutTableMaxRatio = 2.5f;
    
    internal IGridLayout CreateGridLayout_(int mipmapIndex) {
        if (FileResourceTyped is not { } tf || mipmapIndex >= tf.TextureBuffer.MipmapAllocations.Length)
            return new AutoGridLayout(0, 0, 0, 0, 0, 0);
        var w = tf.TextureBuffer.WidthOfMipmap(mipmapIndex);
        var h = tf.TextureBuffer.HeightOfMipmap(mipmapIndex);
        var d = tf.TextureBuffer.DepthOfMipmap(mipmapIndex);
        if (w == 0 || h == 0 || d == 0)
            return EmptyGridLayout.Instance;

        if (d == 6 && tf.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
            return new CubeGridLayout(w, h, 0, 0);
        return new AutoGridLayout(w, h, SliceSpacing.Width, SliceSpacing.Height, d, LayoutTableMaxRatio);
    }
}