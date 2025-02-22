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

        public PreviewHandler(Explorer explorer)
        {
            this._explorer = explorer;
        }

        public void Dispose()
        {
            this.ClearPreview();
        }

        public void ClearPreview()
        {
            this._previewingFile = null;
            this._previewCancellationTokenSource?.Cancel();
            this._previewCancellationTokenSource = null;
            this._previewingFileResource = null;
            this._explorer.ppgPreview.SelectedObject = null;
            this._explorer.hbxPreview.ByteProvider = null;
            this._explorer.bitmapPreview.LoadingFileNameWhenEmpty = null;
            this._explorer.bitmapPreview.ClearFile();
        }

        public bool TryGetAvailableFileResource(
            IVirtualFile file,
            [MaybeNullWhen(false)] out FileResource fileResource)
        {
            fileResource = null!;
            if (!Equals(file, this._previewingFile) || this._previewingFileResource is null)
                return false;

            fileResource = this._previewingFileResource;
            return true;
        }

        public void PreviewFile(IVirtualFile file)
        {
            if (Equals(this._previewingFile, file))
                return;

            this._previewingFile = file;

            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var token = (this._previewCancellationTokenSource = new()).Token;

            this._explorer.bitmapPreview.LoadingFileNameWhenEmpty = file.Name;
            this._explorer.bitmapPreview.ClearFile(true);

            if (this._explorer.Vfs is not { } tree)
                return;
            using var lookup = tree.GetLookup(file);
            lookup.AsFileResource(token)
                .ContinueWith(
                    fr => {
                        if (!Equals(file, this._previewingFile))
                            return;

                        if (!fr.IsCompletedSuccessfully) {
                            this.ClearPreview();
                            return;
                        }

                        this._explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                        this._explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                        if (fr.Result is TexFile tf)
                            this._explorer.bitmapPreview.SetFile(tf);
                        else if (MultiBitmapViewerControl.MaySupportFileName(file.Name))
                            this._explorer.bitmapPreview.SetFile(fr.Result);
                        else {
                            this._explorer.bitmapPreview.LoadingFileNameWhenEmpty = null;
                            this._explorer.bitmapPreview.ClearFile();
                        }
                    },
                    token,
                    TaskContinuationOptions.DenyChildAttach,
                    mainThreadScheduler);
        }
    }
}
