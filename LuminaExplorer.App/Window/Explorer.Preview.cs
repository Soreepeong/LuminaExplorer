using System.Diagnostics.CodeAnalysis;
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
            _explorer.texPreview.ClearFile();
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

                    if (fr.Result is TexFile tf) {
                        if (_explorer.Tree is { } tree)
                            _explorer.texPreview.SetFileAsync(tree, file, tf);
                    }
                }, token);
        }
    }
}
