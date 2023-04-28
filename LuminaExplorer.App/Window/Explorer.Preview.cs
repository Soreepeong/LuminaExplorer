using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class PreviewHandler : IDisposable {
        private readonly Explorer _explorer;

        private IVirtualFile? _previewingFile;
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
            _explorer.bitmapPreview.LoadingFileNameWhenEmpty = null;
            _explorer.bitmapPreview.ClearFile();
        }

        public bool TryGetAvailableFileResource(IVirtualFile file,
            [MaybeNullWhen(false)] out FileResource fileResource) {
            fileResource = null!;
            if (!Equals(file, _previewingFile) || _previewingFileResource is null)
                return false;

            fileResource = _previewingFileResource;
            return true;
        }

        public void PreviewFile(IVirtualFile file) {
            if (Equals(_previewingFile, file))
                return;

            _previewingFile = file;

            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var token = (_previewCancellationTokenSource = new()).Token;

            _explorer.bitmapPreview.LoadingFileNameWhenEmpty = file.Name;
            _explorer.bitmapPreview.ClearFile(true);

            if (_explorer.Vfs is not { } tree)
                return;
            using var lookup = tree.GetLookup(file);
            lookup.AsFileResource(token)
                .ContinueWith(fr => {
                        if (!Equals(file, _previewingFile))
                            return;

                        if (!fr.IsCompletedSuccessfully) {
                            ClearPreview();
                            return;
                        }

                        _explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                        _explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                        if (fr.Result is TexFile tf)
                            _explorer.bitmapPreview.SetFile(tf);
                        else if (MultiBitmapViewerControl.MaySupportFileName(file.Name))
                            _explorer.bitmapPreview.SetFile(fr.Result);
                        else {
                            _explorer.bitmapPreview.LoadingFileNameWhenEmpty = null;
                            _explorer.bitmapPreview.ClearFile();
                        }
                    },
                    token,
                    TaskContinuationOptions.DenyChildAttach,
                    mainThreadScheduler);
        }
    }
}
