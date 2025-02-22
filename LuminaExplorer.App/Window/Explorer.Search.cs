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

        public SearchHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this.Vfs = explorer.Vfs;
            this.AppConfig = explorer._appConfig;
            this._txtSearch = this._explorer.txtSearch.TextBox!;
            this._txtSearch.PlaceholderText = @"Search...";
            this._explorer.btnSearch.Click += this.btnSearch_Click;
            this._explorer.txtSearch.KeyUp += this.txtSearch_KeyUp;
        }

        public void Dispose()
        {
            this._searchCancellationTokenSource.Cancel();
            this._explorer.btnSearch.Click -= this.btnSearch_Click;
            this._explorer.txtSearch.KeyUp -= this.txtSearch_KeyUp;
        }

        public IVirtualFileSystem? Vfs { get; set; }

        public AppConfig AppConfig { get; set; }

        private void btnSearch_Click(object? sender, EventArgs e) => this.Search(this._txtSearch.Text);

        private void txtSearch_KeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) this.Search(this._txtSearch.Text);
        }

        public void SearchAbort()
        {
            if (this._searchCancellationTokenSource.IsCancellationRequested)
                return;

            this._searchCancellationTokenSource.Cancel();
            this._explorer._navigationHandler?.NavigateToCurrent();
        }

        public void Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) {
                this.SearchAbort();
                return;
            }

            this._searchCancellationTokenSource.Cancel();

            if (this._explorer._fileListHandler is null || this._explorer._navigationHandler is null ||
                this.Vfs is null)
                return;

            var cancelSource = this._searchCancellationTokenSource = new();

            this._explorer._fileListHandler.Clear();

            var pendingObjectsLock = new object();
            var pendingObjects1 = new List<VirtualObject>();
            var pendingObjects2 = new List<VirtualObject>();
            var searchBaseFolder = this._explorer._navigationHandler.CurrentFolder;
            if (searchBaseFolder is null)
                return;

            void OnObjectFound(VirtualObject vo)
            {
                cancelSource.Token.ThrowIfCancellationRequested();

                lock (pendingObjectsLock)
                    pendingObjects1.Add(vo);
            }

            void ReportProgress(IVirtualFileSystem.SearchProgress progress)
            {
                cancelSource.Token.ThrowIfCancellationRequested();

                Debug.Print(
                    "{0:0.00}% {1:##.###} / {2:##.###}: {3}",
                    100.0 * progress.Progress / progress.Total,
                    progress.Progress,
                    progress.Total,
                    progress.LastObject);

                if (progress.Completed) {
                    if (pendingObjects1.Any())
                        this._explorer.BeginInvoke(() => this._explorer._fileListHandler?.AddObjects(pendingObjects1));
                    return;
                }

                if (this._explorer._fileListHandler is not { } fileListHandler ||
                    fileListHandler.CurrentFolder is not null) {
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
                    this._explorer.BeginInvoke(() => this._explorer._fileListHandler?.AddObjects(objects));
                    pendingObjects2.Clear();
                }
            }

            this.Vfs.Search(
                searchBaseFolder,
                this._txtSearch.Text,
                ReportProgress,
                folder => {
                    if (this.Vfs is { } tree)
                        OnObjectFound(new(tree, folder));
                    else {
                        cancelSource.Cancel();
                        throw new OperationCanceledException();
                    }
                },
                file => {
                    if (this.Vfs is { } tree)
                        OnObjectFound(new(tree, file));
                    else {
                        cancelSource.Cancel();
                        throw new OperationCanceledException();
                    }
                },
                this.AppConfig.SearchThreads,
                this.AppConfig.SearchEntryTimeout,
                cancelSource.Token);
        }
    }
}
