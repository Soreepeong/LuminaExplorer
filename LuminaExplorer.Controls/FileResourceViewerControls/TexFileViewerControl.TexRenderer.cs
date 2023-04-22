namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private const float LayoutTableMaxRatio = 2.5f;

    private interface ITexRenderer : IDisposable {
        bool HasNondisposedBitmap { get; }
        Size ImageSize { get; }
        LoadState State { get; }
        Exception? LastException { get; }

        void Reset(bool disposeBitmap = true);
        bool Draw(PaintEventArgs e);

        Task LoadFileAsync(int mipIndex);

        public enum LoadState {
            Empty,
            Loading,
            Loaded,
            Error,
        }
    }
}
