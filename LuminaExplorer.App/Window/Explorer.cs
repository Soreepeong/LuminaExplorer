using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Structs;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.LazySqPackTree.Matcher;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private const int ImageListIndexFile = 0;
    private const int ImageListIndexFolder = 1;

    private readonly VirtualSqPackTree _vspTree;

    private readonly ImageList _treeViewImageList;
    private readonly ImageList _listViewImageList;
    private readonly Dictionary<VirtualObject, Task> _listViewImageLoadTasks = new();

    private readonly List<VirtualFolder> _navigationHistory = new();
    private int _navigationHistoryPosition = -1;
    private VirtualFolder _explorerFolder = null!;

    private CancellationTokenSource? _previewCancellationTokenSource;

    public Explorer(VirtualSqPackTree vspTree) {
        _vspTree = vspTree;

        InitializeComponent();

        txtPath.AutoCompleteMode = AutoCompleteMode.Suggest;
        txtPath.AutoCompleteSource = AutoCompleteSource.CustomSource;

        _listViewImageList = new();
        _listViewImageList.ColorDepth = ColorDepth.Depth32Bit;

        lvwFiles.SmallImageList = lvwFiles.LargeImageList = _listViewImageList;
        lvwFiles.VirtualListDataSource = new ExplorerListViewDataSource(lvwFiles);
        lvwFiles.PrimarySortColumn = colFilesName;
        lvwFiles.PrimarySortOrder = SortOrder.Ascending;
        lvwFiles.MouseWheel += lvwFiles_MouseWheel;

        _treeViewImageList = new();
        _treeViewImageList.ColorDepth = ColorDepth.Depth32Bit;

        tvwFiles.ImageList = _treeViewImageList;
        tvwFiles.Nodes.Add(new FolderTreeNode(_vspTree));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];

        ChangeTreeViewImageListDimensions(16);
        ChangeListViewImageListDimensions(16);
        cboView.SelectedIndex = 5; // detail view

        _vspTree.FileChanged += _vspTree_FileChanged;
        _vspTree.FolderChanged += _vspTree_FolderChanged;
        NavigateTo(_vspTree.RootFolder, true);

        // random folder with a lot of images
        // TryNavigateTo("/common/graphics/texture");

        // mustadio
        TryNavigateTo("/chara/monster/m0361/obj/body/b0003/texture/");

        // construct 14
        // TryNavigateTo("/chara/monster/m0489/animation/a0001/bt_common/loop_sp/");
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            _treeViewImageList.Dispose();
            lock (_listViewImageList)
                _listViewImageList.Dispose();
            _vspTree.FileChanged -= _vspTree_FileChanged;
            _vspTree.FolderChanged -= _vspTree_FolderChanged;
            lvwFiles.ClearObjects();
            tvwFiles.Nodes.Clear();

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ChangeTreeViewImageListDimensions(int dimension) {
        if (!_treeViewImageList.Images.Empty &&
            _treeViewImageList.ImageSize.Width == dimension &&
            _treeViewImageList.ImageSize.Height == dimension)
            return;

        _treeViewImageList.Images.Clear();
        _treeViewImageList.ImageSize = new(dimension, dimension);

        var largeIcon = dimension > 16;
        for (var i = 0; i < 2; i++) {
            switch (i) {
                case ImageListIndexFile:
                    using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, largeIcon)!)
                        _treeViewImageList.Images.Add(icon);
                    break;
                case ImageListIndexFolder:
                    using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, largeIcon)!)
                        _treeViewImageList.Images.Add(icon);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    private void ChangeListViewImageListDimensions(int dimension) {
        lock (_listViewImageList) {
            if (!_listViewImageList.Images.Empty &&
                _listViewImageList.ImageSize.Width == dimension &&
                _listViewImageList.ImageSize.Height == dimension)
                return;

            foreach (var k in _listViewImageLoadTasks.Keys)
                k.Image = null;

            _listViewImageLoadTasks.Clear();
            _listViewImageList.Images.Clear();
            _listViewImageList.ImageSize = new(dimension, dimension);

            var largeIcon = dimension > 16;
            for (var i = 0; i < 2; i++) {
                switch (i) {
                    case ImageListIndexFile:
                        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, largeIcon)!)
                            _listViewImageList.Images.Add(icon);
                        break;
                    case ImageListIndexFolder:
                        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, largeIcon)!)
                            _listViewImageList.Images.Add(icon);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }
    }

    // ReSharper disable once UnusedMember.Local
    private List<VirtualFolder> GetSelectedFolders() {
        var folders = new List<VirtualFolder>();
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return folders;

        for (var i = 0; i < lvwFiles.SelectedIndices.Count; i++) {
            var obj = source[lvwFiles.SelectedIndices[i]];
            if (obj.IsFolder)
                folders.Add(obj.Folder);
        }

        return folders;
    }

    private List<VirtualFile> GetSelectedFiles() {
        var folders = new List<VirtualFile>();
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return folders;

        for (var i = 0; i < lvwFiles.SelectedIndices.Count; i++) {
            var obj = source[lvwFiles.SelectedIndices[i]];
            if (!obj.IsFolder)
                folders.Add(obj.File);
        }

        return folders;
    }

    #region Event Handlers

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        switch (keyData) {
            case Keys.Control | Keys.F:
            case Keys.BrowserSearch:
                txtSearch.Focus();
                return true;
            case Keys.F4:
                txtPath.Focus();
                return true;
            case Keys.BrowserBack:
                NavigateBack();
                return true;
            case Keys.BrowserForward:
                NavigateForward();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void VirtualObject_ImageRequested(VirtualObject virtualObject, CancellationToken cancellationToken) {
        if (_listViewImageLoadTasks.ContainsKey(virtualObject))
            return;

        _listViewImageLoadTasks.Add(virtualObject, Task.Run(async () => {
            var lookup = virtualObject.Lookup;
            var file = virtualObject.File;

            var canBeTexture = false;
            canBeTexture |= lookup.Type == FileType.Texture;
            canBeTexture |= file.Name.EndsWith(".atex", StringComparison.InvariantCultureIgnoreCase);

            try {
                // may be an .atex file
                if (!canBeTexture && !file.NameResolved && lookup is { Type: FileType.Standard, Size: > 256 })
                    canBeTexture = true;

                if (!canBeTexture)
                    return;

                int w, h;
                lock (_listViewImageList) {
                    w = _listViewImageList.ImageSize.Width;
                    h = _listViewImageList.ImageSize.Height;
                }

                await using var stream = lookup.CreateStream();
                using var bitmap = await QueuedThumbnailer.Instance.LoadFromTexStream(
                    w,
                    h,
                    stream,
                    _vspTree.PlatformId,
                    cancellationToken).ConfigureAwait(false);

                lock (_listViewImageList) {
                    if (w != _listViewImageList.ImageSize.Width)
                        return;
                    if (h != _listViewImageList.ImageSize.Height)
                        return;
                    if (file.Parent != _explorerFolder)
                        return;

                    _listViewImageList.Images.Add(bitmap);
                    virtualObject.Image = _listViewImageList.Images.Count - 1;
                }

                BeginInvoke(() => lvwFiles.RefreshObject(virtualObject));

                // await Task.Factory.FromAsync(BeginInvoke(() => {
                //     if (bitmap.Width != _listViewImageList.ImageSize.Width)
                //         return;
                //     if (bitmap.Height != _listViewImageList.ImageSize.Height)
                //         return;
                //     if (file.Parent != _explorerFolder)
                //         return;
                //
                //     _listViewImageList.Images.Add(bitmap);
                //     virtualObject.Image = _listViewImageList.Images.Count - 1;
                //     lvwFiles.RefreshObject(virtualObject);
                // }), _ => {
                //     bitmap.Dispose();
                // }).ConfigureAwait(false);
            } catch (Exception e) {
                Debug.WriteLine(e);
            }
        }, cancellationToken));
    }

    private void _vspTree_FileChanged(VirtualFile changedFile) {
        while (_explorerFolder == changedFile.Parent) {
            if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                break;

            if (source.FirstOrDefault(x => x.File == changedFile) is { } modelObject)
                modelObject.Name = changedFile.Name;

            break;
        }
    }

    private void _vspTree_FolderChanged(VirtualFolder changedFolder, VirtualFolder[]? previousPathFromRoot) {
        btnNavUp.Enabled = _explorerFolder.Parent is not null;
        while (previousPathFromRoot is not null) {
            if (tvwFiles.Nodes[0] is not FolderTreeNode node)
                break;

            foreach (var folder in previousPathFromRoot.Skip(1)) {
                if (node.TryFindChildNode(folder, out node) is not true)
                    break;
            }

            if (node.Folder != changedFolder)
                break;

            node.Remove();
            // TODO: node.Parent.Nodes.Remo

            if (tvwFiles.Nodes[0] is not FolderTreeNode newParentNode)
                break;

            foreach (var folder in _vspTree.GetTreeFromRoot(changedFolder).Skip(1)) {
                if (folder == changedFolder.Parent) {
                    newParentNode.Nodes.Add(node);
                    break;
                }

                if (newParentNode.TryFindChildNode(folder, out newParentNode))
                    break;
            }

            break;
        }

        while (_explorerFolder == changedFolder) {
            if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                break;

            if (source.FirstOrDefault(x => x.Folder == changedFolder) is { } modelObject)
                modelObject.Name = changedFolder.Name;

            break;
        }
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
            var path = _vspTree.GetFullPath(_navigationHistory[i]);

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

    private void cboView_SelectedIndexChanged(object sender, EventArgs e) {
        lvwFiles.View = cboView.SelectedIndex switch {
            0 or 1 or 2 => View.LargeIcon,
            3 => View.SmallIcon,
            4 => View.List,
            5 => View.Details,
            _ => View.Details,
        };

        var imageDimension = cboView.SelectedIndex switch {
            0 => 128,
            1 => 64,
            2 => 32,
            3 or 4 or 5 => 16,
            _ => 16,
        };

        ChangeListViewImageListDimensions(imageDimension);
    }

    private void Explorer_Shown(object sender, EventArgs e) {
        lvwFiles.Focus();
    }

    private void txtPath_KeyDown(object sender, KeyEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Enter: {
                    var prevText = txtPath.Text;
                    TryNavigateTo(txtPath.Text)
                        .ContinueWith(_ => {
                            var fullPath = _vspTree.GetFullPath(_explorerFolder);
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
                txtPath.Text = _vspTree.GetFullPath(_explorerFolder);
                lvwFiles.Focus();
                break;
        }
    }

    private void txtPath_KeyUp(object? sender, KeyEventArgs keyEventArgs) {
        var searchedText = txtPath.Text;
        _vspTree.SuggestFullPath(searchedText);

        var cleanerPath = searchedText.Split('/', StringSplitOptions.TrimEntries);
        if (cleanerPath.Any())
            cleanerPath = cleanerPath[..^1];
        _vspTree.AsFoldersResolved(cleanerPath)
            .ContinueWith(res => {
                if (searchedText != txtPath.Text || !res.IsCompletedSuccessfully)
                    return;

                if (txtPath.Tag == res.Result)
                    return;

                txtPath.Tag = res.Result;

                var selectionStart = txtPath.ComboBox!.SelectionStart;
                var selectionLength = txtPath.ComboBox!.SelectionLength;

                var parentFolder = _vspTree.GetFullPath(res.Result);
                var src = new AutoCompleteStringCollection();

                foreach (var f in _vspTree.GetFolders(res.Result).Where(x => x != res.Result.Parent))
                    src.Add($"{parentFolder}{f.Name[..^1]}");
                txtPath.AutoCompleteCustomSource = src;

                txtPath.ComboBox.SelectionStart = selectionStart;
                txtPath.ComboBox.SelectionLength = selectionLength;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void txtSearch_KeyUp(object sender, KeyEventArgs e) {
        var matcher = new MultipleConditionsMatcher();
        matcher.ParseQuery(txtSearch.Text);

        Debug.Print(matcher.ToString());
    }

    private void tvwFiles_AfterExpand(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode ln)
            ExpandFolderTreeNode(ln, true);
    }

    private void tvwFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode node)
            NavigateTo(node.Folder, true);
    }

    private void lvwFiles_MouseWheel(object? sender, MouseEventArgs e) {
        if (ModifierKeys == Keys.Control) {
            cboView.SelectedIndex = e.Delta switch {
                > 0 => Math.Max(0, cboView.SelectedIndex - 1),
                < 0 => Math.Min(cboView.Items.Count - 1, cboView.SelectedIndex + 1),
                _ => cboView.SelectedIndex
            };
        }
    }

    private void lvwFiles_MouseUp(object sender, MouseEventArgs e) {
        switch (e.Button) {
            case MouseButtons.XButton1:
                NavigateBack();
                break;
            case MouseButtons.XButton2:
                NavigateForward();
                break;
        }
    }

    private void lvwFiles_DoubleClick(object sender, EventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;
        if (lvwFiles.SelectedIndices.Count == 0)
            return;

        var vo = source[lvwFiles.SelectedIndices[0]];
        if (vo.IsFolder)
            NavigateTo(vo.Folder, true);
    }

    private void lvwFiles_ItemDrag(object sender, ItemDragEventArgs e) {
        // TODO: export using IStorage, and maybe offer concrete file contents so that it's possible to drag into external hex editors?
        // https://devblogs.microsoft.com/oldnewthing/20080320-00/?p=23063
        // https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-istorage

        var files = GetSelectedFiles();
        if (files.Any()) {
            var virtualFileDataObject = new VirtualFileDataObject();

            // Provide a virtual file (generated on demand) containing the letters 'a'-'z'
            virtualFileDataObject.SetData(files.Select(x => new VirtualFileDataObject.FileDescriptor {
                Name = x.Name,
                Length = _vspTree.GetLookup(x).Size,
                StreamContents = dstStream => {
                    using var srcStream = _vspTree.GetLookup(x).CreateStream();
                    srcStream.CopyTo(dstStream);
                },
            }).ToArray());

            DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
        }
    }

    private void lvwFiles_KeyPress(object sender, KeyPressEventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;
        if (e.KeyChar == (char) Keys.Enter) {
            if (lvwFiles.SelectedIndices.Count == 0)
                return;
            if (source[lvwFiles.SelectedIndices[0]].Folder is { } folder)
                NavigateTo(folder, true);
        }
    }

    private void lvwFiles_KeyUp(object sender, KeyEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Left when e is { Control: false, Alt: true, Shift: false }:
            case Keys.Back when e is { Control: false, Alt: false, Shift: false }:
            case Keys.BrowserBack:
                NavigateBack();
                break;
            case Keys.Right when e is { Control: false, Alt: true, Shift: false }:
            case Keys.BrowserForward:
                NavigateForward();
                break;
            case Keys.Up when e is { Control: false, Alt: true, Shift: false }:
                NavigateUp();
                break;
        }
    }

    private void lvwFiles_SelectionChanged(object sender, EventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;

        _previewCancellationTokenSource?.Cancel();
        ppgPreview.SelectedObject = null;
        hbxPreview.ByteProvider = null;

        if (lvwFiles.SelectedIndices.Count is > 1 or 0)
            return;

        var vo = source[lvwFiles.SelectedIndices[0]];
        if (vo.IsFolder)
            return;

        _previewCancellationTokenSource = new();
        _vspTree.GetLookup(vo.File)
            .AsFileResource(_previewCancellationTokenSource.Token)
            .ContinueWith(fr => {
                if (!fr.IsCompletedSuccessfully)
                    return;
                ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #endregion

    #region Navigation

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
        if (_explorerFolder.Parent is not { } parent)
            return false;
        NavigateTo(parent, true);
        return true;
    }

    private Task<FolderTreeNode> TryNavigateTo(params string[] pathComponents) =>
        TryNavigateToImpl(
            (FolderTreeNode) tvwFiles.Nodes[0],
            Path.Join(pathComponents).Replace('\\', '/').TrimStart('/').Split('/'),
            0);

    private Task<FolderTreeNode> TryNavigateToImpl(FolderTreeNode node, string[] parts, int partIndex) {
        for (; partIndex < parts.Length; partIndex++) {
            var name = parts[partIndex] + "/";
            if (name == "./")
                continue;

            if (name == VirtualFolder.UpFolderKey) {
                node = node.Parent as FolderTreeNode ?? node;
                continue;
            }

            return ExpandFolderTreeNode(node).ContinueWith(_ => {
                var i = 0;
                for (; i < node.Nodes.Count; i++) {
                    if (node.Nodes[i] is FolderTreeNode subnode &&
                        string.Compare(subnode.Folder.Name, name, StringComparison.InvariantCultureIgnoreCase) == 0) {
                        return TryNavigateToImpl(subnode, parts, partIndex + 1);
                    }
                }

                NavigateTo(node.Folder, true);
                return Task.FromResult(node);
            }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        NavigateTo(node.Folder, true);
        return Task.FromResult(node);
    }

    private Task ExpandFolderTreeNode(FolderTreeNode ln, bool expandingNow = false,
        bool neverExpandRecursively = false) {
        if (!expandingNow)
            ln.Expand();

        var resolvedFolder = _vspTree.AsFoldersResolved(ln.Folder);

        if (ln.CallerMustPopulate()) {
            resolvedFolder = resolvedFolder
                .ContinueWith(f => {
                    ln.Nodes.Clear();
                    ln.Nodes.AddRange(_vspTree.GetFolders(ln.Folder)
                        .OrderBy(x => x.Name.ToLowerInvariant())
                        .Select(x => (TreeNode) new FolderTreeNode(_vspTree, x))
                        .ToArray());

                    return f.Result;
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return resolvedFolder.ContinueWith(_ => {
            if (!neverExpandRecursively && _vspTree.GetKnownFolderCount(ln.Folder) == 1) {
                foreach (var n in ln.Nodes)
                    ((TreeNode) n).Expand();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private Task NavigateTo(VirtualFolder folder, bool addToHistory) {
        if (_explorerFolder == folder)
            return Task.CompletedTask;

        _explorerFolder = folder;
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

        var resolveTask = _vspTree.AsFilesResolved(_explorerFolder);
        if (!resolveTask.IsCompleted)
            lvwFiles.SetObjects(Array.Empty<object>());

        txtPath.Text = _vspTree.GetFullPath(folder);

        return resolveTask.ContinueWith(_ => {
            if (_explorerFolder != folder)
                return;

            lock (_listViewImageList) {
                while (_listViewImageList.Images.Count > 2)
                    _listViewImageList.Images.RemoveAt(_listViewImageList.Images.Count - 1);
            }

            lvwFiles.SelectedIndex = -1;

            var objects = _vspTree.GetFolders(folder).Select(x => (object) new VirtualObject(x))
                .Concat(folder.Files.Select(x => (object) new VirtualObject(_vspTree, x)))
                .ToArray();

            foreach (var o in objects) {
                var vo = (VirtualObject) o;
                vo.ImageRequested += VirtualObject_ImageRequested;
            }

            lvwFiles.SetObjects(objects);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #endregion

    private class FolderTreeNode : TreeNode {
        public readonly VirtualFolder Folder;

        private bool _populateTriggered;

        public FolderTreeNode(VirtualSqPackTree tree) : this(tree.RootFolder, @"(root)", true) { }

        public FolderTreeNode(VirtualSqPackTree tree, VirtualFolder folder)
            : this(folder, folder.Name.Trim('/'), !tree.WillFolderNeverHaveSubfolders(folder)) { }

        private FolderTreeNode(VirtualFolder folder, string displayName, bool mayHaveChildren) {
            Text = displayName;
            Folder = folder;
            SelectedImageIndex = ImageIndex = ImageListIndexFolder;
            if (mayHaveChildren)
                Nodes.Add(new TreeNode(@"Expanding..."));
        }

        public bool TryFindChildNode(VirtualFolder folder, out FolderTreeNode childNode) {
            foreach (var node in Nodes) {
                if (node is not FolderTreeNode n)
                    continue;

                if (n.Folder == folder) {
                    childNode = n;
                    return true;
                }
            }

            childNode = null!;
            return false;
        }

        public bool CallerMustPopulate() {
            if (_populateTriggered)
                return false;

            _populateTriggered = true;
            return true;
        }
    }

    private class ExplorerListViewDataSource : AbstractVirtualListDataSource, IReadOnlyList<VirtualObject> {
        private readonly List<VirtualObject> _objects = new();

        public ExplorerListViewDataSource(VirtualObjectListView volv) : base(volv) { }

        public override object GetNthObject(int n) => _objects[n];

        public override int GetObjectCount() => _objects.Count;

        public override int GetObjectIndex(object model) => model is VirtualObject vo ? _objects.IndexOf(vo) : -1;

        public override void PrepareCache(int first, int last) {
            // throw new NotImplementedException();
        }

        public override int SearchText(string value, int first, int last, OLVColumn column)
            => DefaultSearchText(value, first, last, column, this);

        public override void Sort(OLVColumn column, SortOrder order) {
            _objects.Sort((a, b) => {
                var r = a.IsFolder switch {
                    true when !b.IsFolder => -1,
                    false when b.IsFolder => 1,
                    // Case when both are folders:
                    true when b.IsFolder => column.AspectName switch {
                        nameof(VirtualObject.Name) =>
                            string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase),
                        nameof(VirtualObject.PackTypeString) => 0,
                        nameof(VirtualObject.Hash1) => a.Hash1Value.CompareTo(b.Hash1Value),
                        nameof(VirtualObject.Hash2) => 0,
                        nameof(VirtualObject.RawSize) => 0,
                        nameof(VirtualObject.StoredSize) => 0,
                        nameof(VirtualObject.ReservedSize) => 0,
                        _ => 0,
                    },
                    // Case when both are files:
                    _ => column.AspectName switch {
                        nameof(VirtualObject.Name) =>
                            string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase),
                        nameof(VirtualObject.PackTypeString) => a.Lookup.Type.CompareTo(b.Lookup.Type),
                        nameof(VirtualObject.Hash1) => a.Hash1Value.CompareTo(b.Hash1Value),
                        nameof(VirtualObject.Hash2) => a.Hash2Value.CompareTo(b.Hash2Value),
                        nameof(VirtualObject.RawSize) => a.Lookup.Size.CompareTo(b.Lookup.Size),
                        nameof(VirtualObject.StoredSize) => a.Lookup.OccupiedBytes.CompareTo(b.Lookup.OccupiedBytes),
                        nameof(VirtualObject.ReservedSize) => a.Lookup.ReservedBytes.CompareTo(b.Lookup.ReservedBytes),
                        _ => 0,
                    },
                };
                return order switch {
                    SortOrder.None or SortOrder.Ascending => r,
                    SortOrder.Descending => -r,
                    _ => throw new InvalidOperationException(),
                };
            });
        }

        public override void AddObjects(ICollection modelObjects)
            => _objects.AddRange(modelObjects.Cast<VirtualObject>());

        public override void InsertObjects(int index, ICollection modelObjects)
            => _objects.InsertRange(index, modelObjects.Cast<VirtualObject>());

        public override void RemoveObjects(ICollection modelObjects) {
            foreach (var o in modelObjects)
                if (o is VirtualObject vo)
                    _objects.Remove(vo);
        }

        public override void SetObjects(IEnumerable collection) {
            _objects.Clear();
            _objects.AddRange(collection.Cast<VirtualObject>());
        }

        public override void UpdateObject(int index, object modelObject) =>
            _objects[index] = (VirtualObject) modelObject;

        public VirtualObject this[int n] => _objects[n];

        public IEnumerator<VirtualObject> GetEnumerator() => _objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _objects.GetEnumerator();

        public int Count => _objects.Count;
    }

    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
    private sealed class VirtualObject : IDisposable, INotifyPropertyChanged {
        private readonly VirtualFile? _file;
        private readonly VirtualFolder? _folder;
        private readonly Lazy<uint> _hash2;
        private readonly object _imageFallback;

        private CancellationTokenSource? _imageCancellationTokenSource;
        private Lazy<string> _name;
        private Lazy<VirtualFileLookup>? _lookup;
        private object? _imageKey;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file) {
            _imageFallback = ImageListIndexFile;
            _file = file;
            _name = new(() => file.Name);
            _lookup = new(() => tree.GetLookup(File));
            _hash2 = new(() => tree.GetFullPathHash(File));
        }

        public VirtualObject(VirtualFolder folder) {
            _imageFallback = ImageListIndexFolder;
            _folder = folder;
            _name = new(folder.Name.Trim('/'));
            _hash2 = new(0u);
        }

        private void ReleaseUnmanagedResources() {
            if (_lookup is { IsValueCreated: true }) {
                _lookup.Value.Dispose();
                _lookup = null;
            }
        }

        public void Dispose() {
            _imageCancellationTokenSource?.Cancel();
            _imageCancellationTokenSource = null;
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~VirtualObject() {
            ReleaseUnmanagedResources();
        }

        public event Action<VirtualObject, CancellationToken>? ImageRequested;

        public bool IsFolder => _lookup is null;

        public VirtualFile File => _file ?? throw new InvalidOperationException();

        public VirtualFolder Folder => _folder ?? throw new InvalidOperationException();

        public VirtualFileLookup Lookup => _lookup?.Value ?? throw new InvalidOperationException();

        public uint Hash1Value => IsFolder ? Folder.FolderHash : File.FileHash;

        public uint Hash2Value => _hash2.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        public string Name {
            get => _name.Value;
            set => SetField(ref _name, new(value));
        }

        public string PackTypeString => _lookup is null
            ? ""
            : _lookup.Value.Type is var x
                ? x switch {
                    FileType.Empty => "Placeholder",
                    FileType.Standard => "Standard",
                    FileType.Model => "Model",
                    FileType.Texture => "Texture",
                    _ => $"{x}",
                }
                : "<error>";

        public string Hash1 => $"{Hash1Value:X08}";

        public string Hash2 => IsFolder ? "" : $"{Hash2Value:X08}";

        public string RawSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.Size);

        public string StoredSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.OccupiedBytes);

        public string ReservedSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.ReservedBytes);

        [UsedImplicitly]
        public object? Image {
            get {
                if (_imageKey is not null)
                    return _imageKey;

                _imageCancellationTokenSource ??= new();
                ImageRequested?.Invoke(this, _imageCancellationTokenSource.Token);
                return _imageFallback;
            }
            set {
                if (SetField(ref _imageKey, value)) {
                    _imageCancellationTokenSource?.Cancel();
                    _imageCancellationTokenSource = null;
                }
            }
        }

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
