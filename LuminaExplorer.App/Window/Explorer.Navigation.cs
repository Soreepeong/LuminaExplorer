using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class NavigationHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly ComboBox _txtPath;

        private readonly List<IVirtualFolder> _navigationHistory = new();
        private int _navigationHistoryPosition = -1;
        private IVirtualFolder? _currentFolder;

        private IVirtualFileSystem? _vfs;
        private AppConfig _appConfig;

        public NavigationHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this._appConfig = explorer.AppConfig;
            this._txtPath = explorer.txtPath.ComboBox!;
            this._txtPath.AutoCompleteMode = AutoCompleteMode.Suggest;
            this._txtPath.AutoCompleteSource = AutoCompleteSource.CustomSource;

            this._vfs = explorer.Vfs;
            if (this._vfs is not null) this._vfs.FolderChanged += this.SqPackVfsFolderChangedNavigation;

            this._explorer.btnNavBack.Click += this.btnNavBack_Click;
            this._explorer.btnNavForward.Click += this.btnNavForward_Click;
            this._explorer.btnsHistory.DropDownOpening += this.btnsHistory_DropDownOpening;
            this._explorer.btnsHistory.DropDownItemClicked += this.btnsHistory_DropDownItemClicked;
            this._explorer.btnNavUp.Click += this.btnNavUp_Click;
            this._explorer.txtPath.KeyDown += this.txtPath_KeyDown;
            this._explorer.txtPath.KeyUp += this.txtPath_KeyUp;
            this._txtPath.TextUpdate += this.txtPath_TextUpdate;
        }

        public void Dispose()
        {
            this._explorer.btnNavBack.Click -= this.btnNavBack_Click;
            this._explorer.btnNavForward.Click -= this.btnNavForward_Click;
            this._explorer.btnsHistory.DropDownOpening -= this.btnsHistory_DropDownOpening;
            this._explorer.btnsHistory.DropDownItemClicked -= this.btnsHistory_DropDownItemClicked;
            this._explorer.btnNavUp.Click -= this.btnNavUp_Click;
            this._explorer.txtPath.KeyDown -= this.txtPath_KeyDown;
            this._explorer.txtPath.KeyUp -= this.txtPath_KeyUp;
            this._txtPath.TextUpdate -= this.txtPath_TextUpdate;

            this.Vfs = null;
        }

        public IVirtualFileSystem? Vfs {
            get => this._vfs;
            set {
                if (this._vfs == value)
                    return;

                if (this._vfs is not null) this._vfs.FolderChanged -= this.SqPackVfsFolderChangedNavigation;

                this._vfs = value;
                this._currentFolder = null;

                if (this._vfs is not null) {
                    this._vfs.FolderChanged += this.SqPackVfsFolderChangedNavigation;

                    this.NavigateTo(this._vfs.RootFolder, true);
                }
            }
        }

        // ReSharper disable once ConvertToAutoProperty
        public AppConfig AppConfig {
            get => this._appConfig;
            set => this._appConfig = value;
        }

        public IVirtualFolder? CurrentFolder => this._currentFolder;

        private void SqPackVfsFolderChangedNavigation(
            IVirtualFolder changedFolder,
            IVirtualFolder[]? previousPathFromRoot)
        {
            this._explorer.btnNavUp.Enabled = this._currentFolder?.Parent is not null;
        }

        private void btnNavBack_Click(object? sender, EventArgs e) => this.NavigateBack();

        private void btnNavForward_Click(object? sender, EventArgs e) => this.NavigateForward();

        private void btnNavUp_Click(object? sender, EventArgs e) => this.NavigateUp();

        private void btnsHistory_DropDownOpening(object? sender, EventArgs e)
        {
            if (this._vfs is not { } tree)
                return;

            var counter = 0;
            for (int iFrom = Math.Max(0, this._navigationHistoryPosition - 10),
                 iTo = Math.Min(this._navigationHistory.Count - 1, this._navigationHistoryPosition + 10),
                 i = iTo;
                 i >= iFrom;
                 i--, counter++) {
                var path = tree.GetFullPath(this._navigationHistory[i]);

                if (this._explorer.btnsHistory.DropDownItems.Count <= counter) {
                    this._explorer.btnsHistory.DropDownItems.Add(
                        new ToolStripButton {
                            AutoSize = false,
                            Alignment = ToolStripItemAlignment.Left,
                            TextAlign = ContentAlignment.MiddleLeft,
                            Width = this._explorer.LogicalToDeviceUnits(320),
                        });
                }

                var ddi = this._explorer.btnsHistory.DropDownItems[counter];
                ddi.Visible = true;
                ddi.Text = path == "" ? "(root)" : path;
                ddi.Tag = i;
            }

            for (; counter < this._explorer.btnsHistory.DropDownItems.Count; counter++)
                this._explorer.btnsHistory.DropDownItems[counter].Visible = false;
        }

        private void btnsHistory_DropDownItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem?.Tag is int historyIndex)
                this.NavigateTo(this._navigationHistory[this._navigationHistoryPosition = historyIndex], false);
        }

        private void txtPath_KeyDown(object? sender, KeyEventArgs e)
        {
            if (this.IsFilterText) {
                switch (e.KeyCode) {
                    case Keys.Enter:
                    case Keys.Down:
                        e.Handled = e.SuppressKeyPress = true;
                        this._explorer._fileTreeHandler?.FocusFilterResults();
                        return;
                    case Keys.Escape:
                        e.Handled = e.SuppressKeyPress = true;
                        this.ClearFilter();
                        this._explorer._fileListHandler?.Focus();
                        return;
                }

                return;
            }

            switch (e.KeyCode) {
                case Keys.Enter: {
                    var prevText = this._txtPath.Text;
                    this._explorer._fileTreeHandler?.ExpandTreeTo(this._txtPath.Text)
                        .ContinueWith(
                            vfr => {
                                if (!vfr.IsCompletedSuccessfully || this._vfs is not { } tree)
                                    return;

                                var fullPath = tree.GetFullPath(vfr.Result.Folder);
                                var exactMatchFound = 0 == string.Compare(
                                    fullPath.TrimEnd('/'),
                                    prevText.Trim().TrimEnd('/'),
                                    StringComparison.InvariantCultureIgnoreCase);
                                this._txtPath.Text = prevText;

                                if (exactMatchFound) {
                                    this._explorer._fileListHandler?.Focus();
                                    return;
                                }

                                var currentFullPathLength = fullPath.Length;
                                var sharedLength = 0;
                                while (sharedLength < currentFullPathLength && sharedLength < prevText.Length)
                                    sharedLength++;
                                this._txtPath.SelectionStart = sharedLength;
                                this._txtPath.SelectionLength = prevText.Length - sharedLength;
                            },
                            default,
                            TaskContinuationOptions.DenyChildAttach,
                            TaskScheduler.FromCurrentSynchronizationContext());
                    break;
                }

                case Keys.Escape: {
                    if (this._vfs is not { } tree || this._currentFolder is not { } currentFolder)
                        return;

                    this._txtPath.Text = tree.GetFullPath(currentFolder);
                    this._explorer._fileListHandler?.Focus();
                    break;
                }
            }
        }

        /// <summary>
        /// Whether the address bar holds a name filter instead of a path, which is when it does not start with a slash.
        /// </summary>
        private bool IsFilterText => IsFilterTextValue(this._txtPath.Text);

        private static bool IsFilterTextValue(string text) => !string.IsNullOrWhiteSpace(text) && text[0] != '/';

        private void txtPath_TextUpdate(object? sender, EventArgs e) =>
            this._explorer._fileTreeHandler?.SetFilter(IsFilterTextValue(this._txtPath.Text) ? this._txtPath.Text : null);

        private void txtPath_KeyUp(object? sender, KeyEventArgs keyEventArgs)
        {
            if (this._vfs is not { } tree || this.IsFilterText)
                return;

            var searchedText = this._txtPath.Text;
            tree.SuggestFullPath(searchedText);

            var cleanerPath = searchedText.Split('/', StringSplitOptions.TrimEntries);
            if (cleanerPath.Any())
                cleanerPath = cleanerPath[..^1];
            tree.AsFoldersResolved(cleanerPath)
                .ContinueWith(
                    res => {
                        if (this._vfs is not { } tree2)
                            return;

                        if (searchedText != this._txtPath.Text || !res.IsCompletedSuccessfully)
                            return;

                        if (Equals(this._txtPath.Tag, res.Result))
                            return;

                        this._txtPath.Tag = res.Result;

                        var selectionStart = this._txtPath.SelectionStart;
                        var selectionLength = this._txtPath.SelectionLength;

                        var parentFolder = tree2.GetFullPath(res.Result);
                        var src = new AutoCompleteStringCollection();

                        foreach (var f in tree2.GetFolders(res.Result).Where(x => !Equals(x, res.Result.Parent)))
                            src.Add($"{parentFolder}{f.Name[..^1]}");
                        this._txtPath.AutoCompleteCustomSource = src;

                        this._txtPath.SelectionStart = selectionStart;
                        this._txtPath.SelectionLength = selectionLength;
                    },
                    default,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Leaves the name filter mode, and shows the path of the current folder in the address bar.
        /// </summary>
        public void ClearFilter()
        {
            this._explorer._fileTreeHandler?.SetFilter(null);
            if (this._vfs is { } tree && this._currentFolder is { } currentFolder)
                this._txtPath.Text = tree.GetFullPath(currentFolder);
        }

        public bool NavigateBack()
        {
            if (this._navigationHistoryPosition <= 0)
                return false;
            this.NavigateTo(this._navigationHistory[--this._navigationHistoryPosition], false);
            return true;
        }

        public bool NavigateForward()
        {
            if (this._navigationHistoryPosition + 1 >= this._navigationHistory.Count)
                return false;
            this.NavigateTo(this._navigationHistory[++this._navigationHistoryPosition], false);
            return true;
        }

        public bool NavigateUp()
        {
            if (this._currentFolder?.Parent is not { } parent)
                return false;
            this.NavigateTo(parent, true);
            return true;
        }

        public void NavigateToCurrent()
        {
            if (this._explorer._fileListHandler is { } fileListHandler)
                fileListHandler.CurrentFolder = this._currentFolder;
        }

        public void NavigateTo(IVirtualFolder folder, bool addToHistory)
        {
            if (this._vfs is not { } tree)
                return;

            if (Equals(this._currentFolder, folder))
                return;

            this._currentFolder = folder;
            if (addToHistory) {
                this._navigationHistory.RemoveRange(
                    this._navigationHistoryPosition + 1,
                    this._navigationHistory.Count - this._navigationHistoryPosition - 1);
                this._navigationHistory.Add(folder);
                this._navigationHistoryPosition++;

                if (this._navigationHistory.Count > 1000) {
                    var toRemove = this._navigationHistory.Count - 1000;
                    this._navigationHistory.RemoveRange(0, toRemove);
                    this._navigationHistoryPosition -= toRemove;
                    if (this._navigationHistoryPosition < 0) this._navigationHistoryPosition = 0;
                }
            }

            this._explorer.btnNavBack.Enabled = this._navigationHistoryPosition > 0;
            this._explorer.btnNavForward.Enabled = this._navigationHistoryPosition < this._navigationHistory.Count - 1;
            this._explorer.btnNavUp.Enabled = folder.Parent is not null;

            var fullPath = tree.GetFullPath(folder);

            // While filtering, the address bar keeps the filter text.
            if (this._explorer._fileTreeHandler?.IsFiltering is not true)
                this._txtPath.Text = fullPath;

            if (this._explorer._fileListHandler is { } fileListHandler) {
                fileListHandler.CurrentFolder = folder;
                fileListHandler.SetReferencesTargetToCurrentFolder();
            }

            this._explorer.AppConfig = this.AppConfig with {
                LastFolder = fullPath,
            };
        }

        public async Task<IVirtualFolder> NavigateTo(params string[] pathComponents)
        {
            if (this._vfs is null)
                throw new InvalidOperationException();

            var folder = this._vfs.RootFolder;
            foreach (var part in this._vfs.NormalizePath(pathComponents).Split("/")) {
                var folders = this._vfs.GetFolders(await this._vfs.AsFoldersResolved(folder));
                var candidate = folders.FirstOrDefault(
                    x =>
                        string.Compare(x.Name, part + "/", StringComparison.InvariantCultureIgnoreCase) == 0);
                if (candidate is null)
                    break;
                folder = candidate;
            }

            this._explorer.BeginInvoke(() => this.NavigateTo(folder, true));
            return folder;
        }
    }
}
