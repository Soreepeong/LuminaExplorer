using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class PreviewHandler : IDisposable {
        private readonly Explorer _explorer;

        private Task _loadTask = Task.CompletedTask;
        private CancellationTokenSource? _previewCancellationTokenSource;

        public PreviewHandler(Explorer explorer) {
            _explorer = explorer;
        }

        public void Dispose() {
            ClearPreview();
        }

        public void ClearPreview() {
            _previewCancellationTokenSource?.Cancel();
            _previewCancellationTokenSource = null;
            _explorer.ppgPreview.SelectedObject = null;
            _explorer.hbxPreview.ByteProvider = null;
        }

        public void PreviewFile(VirtualFile file) {
            ClearPreview();

            var token = (_previewCancellationTokenSource = new()).Token;

            _loadTask = _loadTask
                .ContinueWith(_ => _explorer.Tree
                        .GetLookup(file)
                        .AsFileResource(token)
                        .ContinueWith(fr => {
                                if (!fr.IsCompletedSuccessfully)
                                    return;
                                _explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                                _explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                            },
                            TaskScheduler.FromCurrentSynchronizationContext()),
                    TaskScheduler.FromCurrentSynchronizationContext())
                .Unwrap();
        }
    }
}
