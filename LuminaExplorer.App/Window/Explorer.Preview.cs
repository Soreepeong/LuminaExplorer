using System.Diagnostics.CodeAnalysis;
using System.Drawing.Imaging;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class PreviewHandler : IDisposable {
        private readonly Explorer _explorer;

        private VirtualFile? _previewingFile;
        private FileResource? _previewingFileResource;
        private CancellationTokenSource? _previewCancellationTokenSource;

        public PreviewHandler(Explorer explorer) {
            _explorer = explorer;
        }

        public void Dispose() {
            ClearPreview();
        }

        public void ClearPreview() {
            _previewingFile = null;
            _previewCancellationTokenSource?.Cancel();
            _previewCancellationTokenSource = null;
            _previewingFileResource = null;
            _explorer.ppgPreview.SelectedObject = null;
            _explorer.hbxPreview.ByteProvider = null;
            _explorer.picPreview.Image?.Dispose();
            _explorer.picPreview.Image = null;
        }

        public bool TryGetAvailableFileResource(VirtualFile file, [MaybeNullWhen(false)] out FileResource fileResource) {
            fileResource = null!;
            if (file != _previewingFile || _previewingFileResource is null)
                return false;
            
            fileResource = _previewingFileResource;
            return true;
        }

        public void PreviewFile(VirtualFile file) {
            if (_previewingFile == file)
                return;

            ClearPreview();

            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var token = (_previewCancellationTokenSource = new()).Token;

            _previewingFile = file;
            _explorer.Tree
                ?.GetLookup(file)
                .AsFileResource(token)
                .ContinueWith(fr => {
                        if (!fr.IsCompletedSuccessfully || _previewingFile != file)
                            return null;
                        _explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                        _explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                        return _previewingFileResource = fr.Result;
                    },
                    token,
                    TaskContinuationOptions.DenyChildAttach,
                    mainThreadScheduler)
                .ContinueWith(fr => {
                    if (!fr.IsCompletedSuccessfully || fr.Result is null || _previewingFile != file)
                        return;

                    if (fr.Result is TexFile tf)
                        PreviewTexFile(file, tf, mainThreadScheduler);
                }, token);
        }

        private unsafe void PreviewTexFile(VirtualFile file, TexFile tf, TaskScheduler mainThreadScheduler) {
            var buf = tf.TextureBuffer.Filter(format: TexFile.TextureFormat.B8G8R8A8);
            Bitmap bitmap;
            fixed (void* p = buf.RawData) {
                using var b = new Bitmap(buf.Width, buf.Height, 4 * buf.Width,
                    PixelFormat.Format32bppArgb, (nint) p);
                bitmap = new(b);
            }

            Task.Factory.StartNew(() => {
                if (_previewingFile != file)
                    bitmap.Dispose();
                else
                    _explorer.picPreview.Image = bitmap;
            }, default, TaskCreationOptions.None, mainThreadScheduler);
        }
    }
}
