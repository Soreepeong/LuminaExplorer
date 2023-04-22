using Lumina.Data.Files;

namespace LuminaExplorer.Controls.FileResourceViewerControls;

public partial class TexFileViewerControl {
    private interface ITexRenderer : IDisposable {
        bool HasNondisposedBitmap { get; }
        Size ImageSize { get; }
        LoadState State { get; }
        Exception? LastException { get; }

        void Reset(bool disposeBitmap = true);
        bool Draw(PaintEventArgs e);

        Task LoadTexFileAsync(TexFile texFile, int mipIndex, int slice);

        public enum LoadState {
            Empty,
            Loading,
            Loaded,
            Error,
        }
    }
}
