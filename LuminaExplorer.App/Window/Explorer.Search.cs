using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class SearchHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly TextBox _txtSearch;
        
        private CancellationTokenSource _searchCancellationTokenSource = new();
        
        public SearchHandler(Explorer explorer) {
            _explorer = explorer;
            Tree = explorer.Tree;
            AppConfig = explorer._appConfig;
            _txtSearch = _explorer.txtSearch.TextBox!;
            _txtSearch.PlaceholderText = @"Search...";
            _explorer.btnSearch.Click += btnSearch_Click;
            _explorer.txtSearch.KeyUp += txtSearch_KeyUp;
        }

        public void Dispose() {
            _searchCancellationTokenSource.Cancel();
            _explorer.btnSearch.Click -= btnSearch_Click;
            _explorer.txtSearch.KeyUp -= txtSearch_KeyUp;
        }
        
        public IVirtualFileSystem? Tree { get; set; }

        public AppConfig AppConfig { get; set; }

        private void btnSearch_Click(object? sender, EventArgs e) => Search(_txtSearch.Text);

        private void txtSearch_KeyUp(object? sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter)
                Search(_txtSearch.Text);
        }

        public void SearchAbort() {
            if (_searchCancellationTokenSource.IsCancellationRequested)
                return;

            _searchCancellationTokenSource.Cancel();
            _explorer._navigationHandler?.NavigateToCurrent();
        }

        public void Search(string query) {
            if (string.IsNullOrWhiteSpace(query)) {
                SearchAbort();
                return;
            }

            _searchCancellationTokenSource.Cancel();

            if (_explorer._fileListHandler is null || _explorer._navigationHandler is null || Tree is null)
                return;

            var cancelSource = _searchCancellationTokenSource = new();

            _explorer._fileListHandler.Clear();

            var pendingObjectsLock = new object();
            var pendingObjects1 = new List<VirtualObject>();
            var pendingObjects2 = new List<VirtualObject>();
            var searchBaseFolder = _explorer._navigationHandler.CurrentFolder;
            if (searchBaseFolder is null)
                return;

            void OnObjectFound(VirtualObject vo) {
                cancelSource.Token.ThrowIfCancellationRequested();

                lock (pendingObjectsLock)
                    pendingObjects1.Add(vo);
            }

            void ReportProgress(IVirtualFileSystem.SearchProgress progress) {
                cancelSource.Token.ThrowIfCancellationRequested();

                Debug.Print("{0:0.00}% {1:##.###} / {2:##.###}: {3}",
                    100.0 * progress.Progress / progress.Total,
                    progress.Progress, progress.Total, progress.LastObject);

                if (progress.Completed) {
                    if (pendingObjects1.Any())
                        _explorer.BeginInvoke(() => _explorer._fileListHandler?.AddObjects(pendingObjects1));
                    return;
                }

                if (_explorer._fileListHandler is not { } fileListHandler || fileListHandler.CurrentFolder is not null)  {
                    cancelSource.Cancel();
                    throw new OperationCanceledException();
                }

                // Defer adding until completion if there simply are too many, since sorting a lot of objects is pretty slow
                if (fileListHandler.ItemCount > 8192 && !progress.Completed)
                    return;

                lock (pendingObjectsLock) {
                    if (!pendingObjects1.Any())
                        return;

                    (pendingObjects1, pendingObjects2) = (pendingObjects2, pendingObjects1);

                    pendingObjects1.Clear();
                    var objects = pendingObjects2.ToArray();
                    _explorer.BeginInvoke(() => _explorer._fileListHandler?.AddObjects(objects));
                    pendingObjects2.Clear();
                }
            }

            Tree.Search(
                searchBaseFolder,
                _txtSearch.Text,
                ReportProgress,
                folder => {
                    if (Tree is { } tree)
                        OnObjectFound(new(tree, folder));
                    else {
                        cancelSource.Cancel();
                        throw new OperationCanceledException();
                    }
                },
                file => {
                    if (Tree is { } tree)
                        OnObjectFound(new(tree, file));
                    else {
                        cancelSource.Cancel();
                        throw new OperationCanceledException();
                    }
                },
                AppConfig.SearchThreads,
                AppConfig.SearchEntryTimeout,
                cancelSource.Token);
        }
    }
}