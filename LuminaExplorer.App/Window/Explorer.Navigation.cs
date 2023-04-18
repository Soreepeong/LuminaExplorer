using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private readonly List<VirtualFolder> _navigationHistory = new();
    private int _navigationHistoryPosition = -1;
    private VirtualFolder _currentFolder;

    private void Constructor_Navigation() {
        txtPath.AutoCompleteMode = AutoCompleteMode.Suggest;
        txtPath.AutoCompleteSource = AutoCompleteSource.CustomSource;
        
        _vsp.FolderChanged += SqPackTree_FolderChanged_Navigation;
    }

    private void Dispose_Navigation() {
        _vsp.FolderChanged -= SqPackTree_FolderChanged_Navigation;
    }

    private void SqPackTree_FolderChanged_Navigation(VirtualFolder changedFolder, VirtualFolder[]? previousPathFromRoot) {
        btnNavUp.Enabled = _currentFolder.Parent is not null;
    }

    private void btnNavBack_Click(object sender, EventArgs e) => NavigateBack();

    private void btnNavForward_Click(object sender, EventArgs e) => NavigateForward();

    private void btnNavUp_Click(object sender, EventArgs e) => NavigateUp();

    private void btnsHistory_DropDownOpening(object sender, EventArgs e) {
        var counter = 0;
        for (int iFrom = Math.Max(0, _navigationHistoryPosition - 10),
             iTo = Math.Min(_navigationHistory.Count - 1, _navigationHistoryPosition + 10),
             i = iTo;
             i >= iFrom;
             i--, counter++) {
            var path = _vsp.GetFullPath(_navigationHistory[i]);

            if (btnsHistory.DropDownItems.Count <= counter) {
                btnsHistory.DropDownItems.Add(new ToolStripButton() {
                    AutoSize = false,
                    Alignment = ToolStripItemAlignment.Left,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Width = 320,
                });
            }

            var ddi = btnsHistory.DropDownItems[counter];
            ddi.Visible = true;
            ddi.Text = path == "" ? "(root)" : path;
            ddi.Tag = i;
        }

        for (; counter < btnsHistory.DropDownItems.Count; counter++)
            btnsHistory.DropDownItems[counter].Visible = false;
    }

    private void btnsHistory_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e) {
        if (e.ClickedItem?.Tag is int historyIndex)
            NavigateTo(_navigationHistory[_navigationHistoryPosition = historyIndex], false);
    }

    private void txtPath_KeyDown(object sender, KeyEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Enter: {
                var prevText = txtPath.Text;
                ExpandTreeTo(txtPath.Text)
                    .ContinueWith(vfr => {
                        var fullPath = _vsp.GetFullPath(vfr.Result.Folder);
                        var exactMatchFound = 0 == string.Compare(
                            fullPath.TrimEnd('/'),
                            prevText.Trim().TrimEnd('/'),
                            StringComparison.InvariantCultureIgnoreCase);
                        txtPath.Text = prevText;

                        if (exactMatchFound) {
                            lvwFiles.Focus();
                            return;
                        }

                        var currentFullPathLength = fullPath.Length;
                        var sharedLength = 0;
                        while (sharedLength < currentFullPathLength && sharedLength < prevText.Length)
                            sharedLength++;
                        txtPath.ComboBox!.SelectionStart = sharedLength;
                        txtPath.ComboBox!.SelectionLength = prevText.Length - sharedLength;
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                break;
            }

            case Keys.Escape:
                txtPath.Text = _vsp.GetFullPath(_currentFolder);
                lvwFiles.Focus();
                break;
        }
    }

    private void txtPath_KeyUp(object? sender, KeyEventArgs keyEventArgs) {
        var searchedText = txtPath.Text;
        _vsp.SuggestFullPath(searchedText);

        var cleanerPath = searchedText.Split('/', StringSplitOptions.TrimEntries);
        if (cleanerPath.Any())
            cleanerPath = cleanerPath[..^1];
        _vsp.AsFoldersResolved(cleanerPath)
            .ContinueWith(res => {
                if (searchedText != txtPath.Text || !res.IsCompletedSuccessfully)
                    return;

                if (txtPath.Tag == res.Result)
                    return;

                txtPath.Tag = res.Result;

                var selectionStart = txtPath.ComboBox!.SelectionStart;
                var selectionLength = txtPath.ComboBox!.SelectionLength;

                var parentFolder = _vsp.GetFullPath(res.Result);
                var src = new AutoCompleteStringCollection();

                foreach (var f in _vsp.GetFolders(res.Result).Where(x => x != res.Result.Parent))
                    src.Add($"{parentFolder}{f.Name[..^1]}");
                txtPath.AutoCompleteCustomSource = src;

                txtPath.ComboBox.SelectionStart = selectionStart;
                txtPath.ComboBox.SelectionLength = selectionLength;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool NavigateBack() {
        if (_navigationHistoryPosition <= 0)
            return false;
        NavigateTo(_navigationHistory[--_navigationHistoryPosition], false);
        return true;
    }

    private bool NavigateForward() {
        if (_navigationHistoryPosition + 1 >= _navigationHistory.Count)
            return false;
        NavigateTo(_navigationHistory[++_navigationHistoryPosition], false);
        return true;
    }

    private bool NavigateUp() {
        if (_currentFolder.Parent is not { } parent)
            return false;
        NavigateTo(parent, true);
        return true;
    }

    private void NavigateTo(VirtualFolder folder, bool addToHistory) {
        if (_currentFolder == folder)
            return;

        colFilesFullPath.IsVisible = false;

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

        btnNavBack.Enabled = _navigationHistoryPosition > 0;
        btnNavForward.Enabled = _navigationHistoryPosition < _navigationHistory.Count - 1;
        btnNavUp.Enabled = folder.Parent is not null;

        txtPath.Text = _vsp.GetFullPath(folder);

        if (lvwFiles.VirtualListDataSource is ExplorerListViewDataSource source)
            source.CurrentFolder = folder;
    }
}