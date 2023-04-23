﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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
            _explorer.texPreview.LoadingFileNameWhenEmpty = null;
            _explorer.texPreview.ClearFile();
        }

        public bool TryGetAvailableFileResource(VirtualFile file,
            [MaybeNullWhen(false)] out FileResource fileResource) {
            fileResource = null!;
            if (file != _previewingFile || _previewingFileResource is null)
                return false;

            fileResource = _previewingFileResource;
            return true;
        }

        public void PreviewFile(VirtualFile file) {
            if (_previewingFile == file)
                return;

            _previewingFile = file;

            var mainThreadScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var token = (_previewCancellationTokenSource = new()).Token;

            _explorer.texPreview.LoadingFileNameWhenEmpty = file.Name;
            _explorer.texPreview.ClearFile(true);

            if (_explorer.Tree is not { } tree)
                return;
            using var lookup = tree.GetLookup(file);
            lookup.AsFileResource(token)
                .ContinueWith(fr => {
                        if (_previewingFile != file || _explorer.Tree is not { } tree2)
                            return;

                        if (!fr.IsCompletedSuccessfully) {
                            ClearPreview();
                            return;
                        }

                        _explorer.ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                        _explorer.hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                        if (fr.Result is TexFile tf)
                            _explorer.texPreview.SetFile(tree2, file, tf);
                        else {
                            _explorer.texPreview.LoadingFileNameWhenEmpty = null;
                            _explorer.texPreview.ClearFile();
                        }
                    },
                    token,
                    TaskContinuationOptions.DenyChildAttach,
                    mainThreadScheduler);
        }
    }
}
