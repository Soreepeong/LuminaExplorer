using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class SearchHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly TextBox _txtSearch;
        
        private CancellationTokenSource _searchCancellationTokenSource = new();
        
        public SearchHandler(Explorer explorer) {
            _explorer = explorer;
            _txtSearch = _explorer.txtSearch.TextBox!;
            _explorer.btnSearch.Click += btnSearch_Click;
            _explorer.txtSearch.KeyUp += txtSearch_KeyUp;
        }

        public void Dispose() {
            _searchCancellationTokenSource.Cancel();
            _explorer.btnSearch.Click -= btnSearch_Click;
            _explorer.txtSearch.KeyUp -= txtSearch_KeyUp;
        }
        
        public VirtualSqPackTree? Tree { get; set; }
        
        private void btnSearch_Click(object? sender, EventArgs e) => Search(_txtSearch.Text);

        private void txtSearch_KeyUp(object? sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Enter)
                Search(_txtSearch.Text);
        }

        private void Search(string query) {
            _searchCancellationTokenSource.Cancel();

            if (string.IsNullOrWhiteSpace(query)) {
                _explorer._navigationHandler?.NavigateToCurrent();
                return;
            }

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

            void ReportProgress(VirtualSqPackTree.SearchProgress progress) {
                cancelSource.Token.ThrowIfCancellationRequested();

                Debug.Print("{0:0.00}% {1:##.###} / {2:##.###}: {3}",
                    100.0 * progress.Progress / progress.Total,
                    progress.Progress, progress.Total, progress.LastObject);

                if (progress.Completed) {
                    if (pendingObjects1.Any())
                        _explorer.BeginInvoke(() => _explorer._fileListHandler?.AddObjects(pendingObjects1));
                    return;
                }

                if (_explorer._fileListHandler is not { } fileListHandler)  {
                    cancelSource.Cancel();
                    throw new OperationCanceledException();
                }

                // Defer adding until completion if there simply are too many, since sorting a lot of objects is pretty slow
                if (fileListHandler.ItemCount > 1000 && !progress.Completed)
                    return;

                // There are too many; Give Up(tm)
                if (fileListHandler.ItemCount + pendingObjects1.Count > 10000) {
                    cancelSource.Cancel();
                    cancelSource.Token.ThrowIfCancellationRequested();
                    return;
                }

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
                TimeSpan.FromSeconds(1000),
                cancelSource.Token);
        }
    }
}