using System.Diagnostics;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private void btnSearch_Click(object sender, EventArgs e) => Search(txtSearch.Text);

    private void txtSearch_KeyUp(object sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Enter)
            Search(txtSearch.Text);
    }

    private CancellationTokenSource _searchCancellationTokenSource = new();
    private Task _searchTask = Task.CompletedTask;

    private void Search(string query) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;

        _searchCancellationTokenSource.Cancel();

        if (string.IsNullOrWhiteSpace(query)) {
            source.CurrentFolder = _currentFolder;
            colFilesFullPath.IsVisible = false;
            return;
        }

        source.CurrentFolder = null;
        
        var cancelSource = _searchCancellationTokenSource = new();

        void StartNewTask() {
            cancelSource.Token.ThrowIfCancellationRequested();

            lvwFiles.ClearObjects();

            colFilesFullPath.IsVisible = true;

            var pendingObjectsLock = new object();
            var pendingObjects1 = new List<VirtualObject>();
            var pendingObjects2 = new List<VirtualObject>();
            var searchBaseFolder = _currentFolder;

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
                        BeginInvoke(() => lvwFiles.AddObjects(pendingObjects1));
                    return;
                }

                // Defer adding until completion if there simply are too many, since sorting a lot of objects is pretty slow
                if (source.Count > 1000 && !progress.Completed)
                    return;

                // There are too many; Give Up(tm)
                if (source.Count + pendingObjects1.Count > 10000) {
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
                    BeginInvoke(() => lvwFiles.AddObjects(objects));
                    pendingObjects2.Clear();
                }
            }

            _searchTask = _vsp.Search(
                    searchBaseFolder,
                    txtSearch.Text,
                    ReportProgress,
                    folder => OnObjectFound(new(_vsp, folder)),
                    file => OnObjectFound(new(_vsp, file)),
                    TimeSpan.FromSeconds(1000),
                    cancelSource.Token);
        }

        _searchTask.ContinueWith(_ => StartNewTask(), TaskScheduler.FromCurrentSynchronizationContext());
    }
}
