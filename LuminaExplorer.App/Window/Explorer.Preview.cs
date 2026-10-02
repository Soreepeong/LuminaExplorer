using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;
using LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed partial class PreviewHandler : IDisposable {
        private readonly Explorer _explorer;

        // Shares the panel with the bitmap preview; only one of them is visible at a time.
        private readonly ScdPlayerControl _scdPreview;

        private IVirtualFile? _previewingFile;
        private FileResource? _previewingFileResource;
        private CancellationTokenSource? _previewCancellationTokenSource;

        public PreviewHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this._scdPreview = new() {
                AutoPlay = false,
                Dock = DockStyle.Fill,
                Visible = false,
            };
            this._explorer.splPreview.Panel2.Controls.Add(this._scdPreview);
            this.InitializeReferences();
        }

        public void Dispose()
        {
            this.ClearPreview();
            this.DisposeReferences();
            this._scdPreview.Dispose();
        }

        private void ShowScdPreview(ScdFile? scd)
        {
            if (scd is null) {
                // Releases the audio device.
                this._scdPreview.ClearFile();
                this._scdPreview.Visible = false;
                this._explorer.bitmapPreview.Visible = true;
            } else {
                this._scdPreview.SetFile(scd);
                this._scdPreview.Visible = true;
                this._explorer.bitmapPreview.Visible = false;
            }
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
            this.ShowScdPreview(null);
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
            this._previewCancellationTokenSource?.Cancel();
            this._previewingFileResource = null;
            var token = (this._previewCancellationTokenSource = new()).Token;

            this._explorer.bitmapPreview.LoadingFileNameWhenEmpty = file.Name;
            this._explorer.bitmapPreview.ClearFile(true);
            this.ShowScdPreview(null);

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

                        this._previewingFileResource = fr.Result;
                        this._explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                        this._explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                        if (fr.Result is ScdFile scd)
                            this.ShowScdPreview(scd);
                        else if (fr.Result is TexFile tf)
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
