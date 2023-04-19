using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class NavigationHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly ComboBox _txtPath;

        private readonly List<VirtualFolder> _navigationHistory = new();
        private int _navigationHistoryPosition = -1;
        private VirtualFolder? _currentFolder;

        private VirtualSqPackTree? _tree;
        private AppConfig _appConfig;

        public NavigationHandler(Explorer explorer) {
            _explorer = explorer;
            _appConfig = explorer.AppConfig;
            _txtPath = explorer.txtPath.ComboBox!;
            _txtPath.AutoCompleteMode = AutoCompleteMode.Suggest;
            _txtPath.AutoCompleteSource = AutoCompleteSource.CustomSource;

            _tree = explorer.Tree;
            if (_tree is not null)
                _tree.FolderChanged += SqPackTree_FolderChanged_Navigation;

            _explorer.btnNavBack.Click += btnNavBack_Click;
            _explorer.btnNavForward.Click += btnNavForward_Click;
            _explorer.btnsHistory.DropDownOpening += btnsHistory_DropDownOpening;
            _explorer.btnsHistory.DropDownItemClicked += btnsHistory_DropDownItemClicked;
            _explorer.btnNavUp.Click += btnNavUp_Click;
            _explorer.txtPath.KeyDown += txtPath_KeyDown;
            _explorer.txtPath.KeyUp += txtPath_KeyUp;
        }

        public void Dispose() {
            _explorer.btnNavBack.Click -= btnNavBack_Click;
            _explorer.btnNavForward.Click -= btnNavForward_Click;
            _explorer.btnsHistory.DropDownOpening -= btnsHistory_DropDownOpening;
            _explorer.btnsHistory.DropDownItemClicked -= btnsHistory_DropDownItemClicked;
            _explorer.btnNavUp.Click -= btnNavUp_Click;
            _explorer.txtPath.KeyDown -= txtPath_KeyDown;
            _explorer.txtPath.KeyUp -= txtPath_KeyUp;

            Tree = null;
        }

        public VirtualSqPackTree? Tree {
            get => _tree;
            set {
                if (_tree == value)
                    return;

                if (_tree is not null)
                    _tree.FolderChanged -= SqPackTree_FolderChanged_Navigation;

                _tree = value;
                _currentFolder = null;

                if (_tree is not null) {
                    _tree.FolderChanged += SqPackTree_FolderChanged_Navigation;

                    NavigateTo(_tree.RootFolder, true);
                }
            }
        }

        // ReSharper disable once ConvertToAutoProperty
        public AppConfig AppConfig {
            get => _appConfig;
            set => _appConfig = value;
        }

        public VirtualFolder? CurrentFolder => _currentFolder;

        private void SqPackTree_FolderChanged_Navigation(VirtualFolder changedFolder,
            VirtualFolder[]? previousPathFromRoot) {
            _explorer.btnNavUp.Enabled = _currentFolder?.Parent is not null;
        }

        private void btnNavBack_Click(object? sender, EventArgs e) => NavigateBack();

        private void btnNavForward_Click(object? sender, EventArgs e) => NavigateForward();

        private void btnNavUp_Click(object? sender, EventArgs e) => NavigateUp();

        private void btnsHistory_DropDownOpening(object? sender, EventArgs e) {
            if (_tree is not { } tree)
                return;

            var counter = 0;
            for (int iFrom = Math.Max(0, _navigationHistoryPosition - 10),
                 iTo = Math.Min(_navigationHistory.Count - 1, _navigationHistoryPosition + 10),
                 i = iTo;
                 i >= iFrom;
                 i--, counter++) {
                var path = tree.GetFullPath(_navigationHistory[i]);

                if (_explorer.btnsHistory.DropDownItems.Count <= counter) {
                    _explorer.btnsHistory.DropDownItems.Add(new ToolStripButton() {
                        AutoSize = false,
                        Alignment = ToolStripItemAlignment.Left,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Width = 320,
                    });
                }

                var ddi = _explorer.btnsHistory.DropDownItems[counter];
                ddi.Visible = true;
                ddi.Text = path == "" ? "(root)" : path;
                ddi.Tag = i;
            }

            for (; counter < _explorer.btnsHistory.DropDownItems.Count; counter++)
                _explorer.btnsHistory.DropDownItems[counter].Visible = false;
        }

        private void btnsHistory_DropDownItemClicked(object? sender, ToolStripItemClickedEventArgs e) {
            if (e.ClickedItem?.Tag is int historyIndex)
                NavigateTo(_navigationHistory[_navigationHistoryPosition = historyIndex], false);
        }

        private void txtPath_KeyDown(object? sender, KeyEventArgs e) {
            switch (e.KeyCode) {
                case Keys.Enter: {
                    var prevText = _txtPath.Text;
                    _explorer._fileTreeHandler?.ExpandTreeTo(_txtPath.Text)
                        .ContinueWith(
                            vfr => {
                                if (_tree is not { } tree)
                                    return;

                                var fullPath = tree.GetFullPath(vfr.Result.Folder);
                                var exactMatchFound = 0 == string.Compare(
                                    fullPath.TrimEnd('/'),
                                    prevText.Trim().TrimEnd('/'),
                                    StringComparison.InvariantCultureIgnoreCase);
                                _txtPath.Text = prevText;

                                if (exactMatchFound) {
                                    _explorer._fileListHandler?.Focus();
                                    return;
                                }

                                var currentFullPathLength = fullPath.Length;
                                var sharedLength = 0;
                                while (sharedLength < currentFullPathLength && sharedLength < prevText.Length)
                                    sharedLength++;
                                _txtPath.SelectionStart = sharedLength;
                                _txtPath.SelectionLength = prevText.Length - sharedLength;
                            }, default,
                            TaskContinuationOptions.DenyChildAttach,
                            TaskScheduler.FromCurrentSynchronizationContext());
                    break;
                }

                case Keys.Escape: {
                    if (_tree is not { } tree || _currentFolder is not { } currentFolder)
                        return;

                    _txtPath.Text = tree.GetFullPath(currentFolder);
                    _explorer._fileListHandler?.Focus();
                    break;
                }
            }
        }

        private void txtPath_KeyUp(object? sender, KeyEventArgs keyEventArgs) {
            if (_tree is not { } tree)
                return;

            var searchedText = _txtPath.Text;
            tree.SuggestFullPath(searchedText);

            var cleanerPath = searchedText.Split('/', StringSplitOptions.TrimEntries);
            if (cleanerPath.Any())
                cleanerPath = cleanerPath[..^1];
            tree.AsFoldersResolved(cleanerPath)
                .ContinueWith(
                    res => {
                        if (_tree is not { } tree2)
                            return;

                        if (searchedText != _txtPath.Text || !res.IsCompletedSuccessfully)
                            return;

                        if (_txtPath.Tag == res.Result)
                            return;

                        _txtPath.Tag = res.Result;

                        var selectionStart = _txtPath.SelectionStart;
                        var selectionLength = _txtPath.SelectionLength;

                        var parentFolder = tree2.GetFullPath(res.Result);
                        var src = new AutoCompleteStringCollection();

                        foreach (var f in tree2.GetFolders(res.Result).Where(x => x != res.Result.Parent))
                            src.Add($"{parentFolder}{f.Name[..^1]}");
                        _txtPath.AutoCompleteCustomSource = src;

                        _txtPath.SelectionStart = selectionStart;
                        _txtPath.SelectionLength = selectionLength;
                    }, default,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        public bool NavigateBack() {
            if (_navigationHistoryPosition <= 0)
                return false;
            NavigateTo(_navigationHistory[--_navigationHistoryPosition], false);
            return true;
        }

        public bool NavigateForward() {
            if (_navigationHistoryPosition + 1 >= _navigationHistory.Count)
                return false;
            NavigateTo(_navigationHistory[++_navigationHistoryPosition], false);
            return true;
        }

        public bool NavigateUp() {
            if (_currentFolder?.Parent is not { } parent)
                return false;
            NavigateTo(parent, true);
            return true;
        }

        public void NavigateToCurrent() {
            if (_explorer._fileListHandler is { } fileListHandler)
                fileListHandler.CurrentFolder = _currentFolder;
        }

        public void NavigateTo(VirtualFolder folder, bool addToHistory) {
            if (_tree is not { } tree)
                return;

            if (_currentFolder == folder)
                return;

            _currentFolder = folder;
            if (addToHistory) {
                _navigationHistory.RemoveRange(
                    _navigationHistoryPosition + 1,
                    _navigationHistory.Count - _navigationHistoryPosition - 1);
                _navigationHistory.Add(folder);
                _navigationHistoryPosition++;

                if (_navigationHistory.Count > 1000) {
                    var toRemove = _navigationHistory.Count - 1000;
                    _navigationHistory.RemoveRange(0, toRemove);
                    _navigationHistoryPosition -= toRemove;
                    if (_navigationHistoryPosition < 0)
                        _navigationHistoryPosition = 0;
                }
            }

            _explorer.btnNavBack.Enabled = _navigationHistoryPosition > 0;
            _explorer.btnNavForward.Enabled = _navigationHistoryPosition < _navigationHistory.Count - 1;
            _explorer.btnNavUp.Enabled = folder.Parent is not null;

            var fullPath = _txtPath.Text = tree.GetFullPath(folder);

            if (_explorer._fileListHandler is { } fileListHandler)
                fileListHandler.CurrentFolder = folder;

            _explorer.AppConfig = AppConfig with {
                LastFolder = fullPath,
            };
        }

        public async Task<VirtualFolder> NavigateTo(params string[] pathComponents) {
            if (_tree is null)
                throw new InvalidOperationException();

            var folder = _tree.RootFolder;
            foreach (var part in VirtualSqPackTree.NormalizePath(pathComponents).Split("/")) {
                var folders = _tree.GetFolders(await _tree.AsFoldersResolved(folder));
                var candidate = folders.FirstOrDefault(x =>
                    string.Compare(x.Name, part, StringComparison.InvariantCultureIgnoreCase) == 0);
                if (candidate is null)
                    break;
                folder = candidate;
            }

            _explorer.BeginInvoke(() => NavigateTo(folder, true));
            return folder;
        }
    }
}
