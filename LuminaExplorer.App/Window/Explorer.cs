using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Structs;
using Lumina.Misc;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer : Form {
    private readonly VirtualSqPackTree _vspTree;

    private readonly ImageList _treeViewImageList;
    private readonly ImageList _listViewImageList;

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
        _listViewImageList.ImageSize = new(64, 64);

        lvwFiles.SmallImageList = lvwFiles.LargeImageList = _listViewImageList;
        lvwFiles.VirtualListDataSource = new ExplorerListViewDataSource(lvwFiles);
        lvwFiles.PrimarySortColumn = colFilesName;
        lvwFiles.PrimarySortOrder = SortOrder.Ascending;
        lvwFiles.MouseWheel += lvwFiles_MouseWheel;

        _treeViewImageList = new();
        _treeViewImageList.ColorDepth = ColorDepth.Depth32Bit;
        _treeViewImageList.Images.Add(UiUtils.ExtractPeIcon("shell32.dll", 0, false)!);
        _treeViewImageList.Images.Add(UiUtils.ExtractPeIcon("shell32.dll", 4, false)!);

        tvwFiles.ImageList = _treeViewImageList;
        tvwFiles.Nodes.Add(new FolderTreeNode(vspTree.RootFolder, @"(root)", true));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];

        cboView.SelectedIndex = 5;

        _vspTree.FileResolved += _vspTree_FileResolved;
        _vspTree.FolderResolved += _vspTree_FolderResolved;
        NavigateTo(vspTree.RootFolder, true);

        // mustadio
        // TryNavigateTo("/chara/monster/m0361/obj/body/b0003/texture/");

        // construct 14
        // TryNavigateTo("/chara/monster/m0489/animation/a0001/bt_common/loop_sp/");
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

    private void _vspTree_FileResolved(VirtualFile file) {
        // TODO
    }
    
    private void _vspTree_FolderResolved(VirtualFolder folder) {
        // TODO
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
            var path = _navigationHistory[i].FullPath;

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

        _listViewImageList.ImageSize = new(imageDimension, imageDimension);

        var folder = _explorerFolder;
        var selectedFolders = new List<VirtualFolder>();
        var selectedFiles = new List<VirtualFile>();
        foreach (var selobj in lvwFiles.SelectedObjects) {
            if (selobj is not VirtualObject obj)
                continue;
            if (obj.Folder is { } selectedFolder)
                selectedFolders.Add(selectedFolder);
            if (obj.File is { } selectedFile)
                selectedFiles.Add(selectedFile);
        }

        _explorerFolder = null!;
        _listViewImageList.Images.Clear();
        NavigateTo(folder, false)
            .ContinueWith(_ => {
                if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                    return;

                lvwFiles.SelectedObjects = source
                    .Select(x => (x.IsFolder && selectedFolders.Any(y => y == x.Folder)) ||
                                 (!x.IsFolder && selectedFiles.Any(y => y == x.File)))
                    .ToList();
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Explorer_FormClosed(object sender, FormClosedEventArgs e) {
        _treeViewImageList.Dispose();
        _listViewImageList.Dispose();
        _vspTree.FileResolved -= _vspTree_FileResolved;
        _vspTree.FolderResolved -= _vspTree_FolderResolved;
    }

    private void Explorer_Shown(object sender, EventArgs e) {
        lvwFiles.Focus();
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

                var parentFolder = res.Result.FullPath;
                var src = new AutoCompleteStringCollection();
                foreach (var f in res.Result.Folders.Values.Where(x => x != res.Result.Parent))
                    src.Add($"{parentFolder}{f.Name[..^1]}");
                txtPath.AutoCompleteCustomSource = src;

                txtPath.ComboBox.SelectionStart = selectionStart;
                txtPath.ComboBox.SelectionLength = selectionLength;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void txtPath_KeyDown(object sender, KeyEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Enter: {
                    var prevText = txtPath.Text;
                    TryNavigateTo(txtPath.Text)
                        .ContinueWith(_ => {
                            var exactMatchFound = 0 == string.Compare(
                                _explorerFolder.FullPath.TrimEnd('/'),
                                prevText.Trim().TrimEnd('/'),
                                StringComparison.InvariantCultureIgnoreCase);
                            txtPath.Text = prevText;

                            if (exactMatchFound) {
                                lvwFiles.Focus();
                                return;
                            }

                            var currentFullPathLength = _explorerFolder.FullPath.Length;
                            var sharedLength = 0;
                            while (sharedLength < currentFullPathLength && sharedLength < prevText.Length)
                                sharedLength++;
                            txtPath.ComboBox!.SelectionStart = sharedLength;
                            txtPath.ComboBox!.SelectionLength = prevText.Length - sharedLength;
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                    break;
                }

            case Keys.Escape:
                txtPath.Text = _explorerFolder.FullPath;
                lvwFiles.Focus();
                break;
        }
    }

    private void tvwFiles_AfterExpand(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode ln) {
            if (ln.ShouldExpandRecursively()) {
                BeginInvoke(() => {
                    foreach (var n in e.Node.Nodes)
                        ((TreeNode) n).Expand();
                });
            }
        }
    }

    private void tvwFiles_BeforeExpand(object? sender, TreeViewCancelEventArgs e) {
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
        if (source[lvwFiles.SelectedIndices[0]].Folder is { } folder)
            NavigateTo(folder, true);
    }

    private void lvwFiles_ItemDrag(object sender, ItemDragEventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;

        var folders = new List<VirtualFolder>();
        var files = new List<VirtualFile>();
        for (var i = 0; i < lvwFiles.SelectedIndices.Count; i++) {
            var obj = source[lvwFiles.SelectedIndices[i]];
            if (obj.Folder is { } folder)
                folders.Add(folder);
            if (obj.File is { } file)
                files.Add(file);
        }

        // TODO: export using IStorage, and maybe offer concrete file contents so that it's possible to drag into external hex editors?
        // https://devblogs.microsoft.com/oldnewthing/20080320-00/?p=23063
        // https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-istorage

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

        if (source[lvwFiles.SelectedIndices[0]].File is { } file) {
            _previewCancellationTokenSource = new();
            _vspTree.GetLookup(file)
                .AsFileResource(_previewCancellationTokenSource.Token)
                .ContinueWith(fr => {
                    if (!fr.IsCompletedSuccessfully)
                        return;
                    ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                    hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
                }, TaskScheduler.FromCurrentSynchronizationContext());

            // var isFocused = lvwFiles.Focused;
            // if (isFocused)
            //     lvwFiles.Focus();
        }
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

        if (!ln.CallerMustPopulate()) {
            return _vspTree.AsFoldersResolved(ln.Folder)
                .ContinueWith(_ => {
                    if (!neverExpandRecursively && ln.ShouldExpandRecursively()) {
                        foreach (var n in ln.Nodes)
                            ((TreeNode) n).Expand();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return _vspTree.AsFoldersResolved(ln.Folder)
            .ContinueWith(_ => {
                ln.Nodes.Clear();
                ln.Nodes.AddRange(ln.Folder.Folders
                    .Where(x => x.Value != ln.Folder.Parent)
                    .OrderBy(x => x.Key.ToLowerInvariant())
                    .Select(x =>
                        (TreeNode) new FolderTreeNode(x.Value, x.Key,
                            !_vspTree.WillFolderNeverHaveSubfolders(x.Value)))
                    .ToArray());

                if (!neverExpandRecursively && ln.ShouldExpandRecursively()) {
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

        txtPath.Text = folder.FullPath;

        return resolveTask.ContinueWith(_ => {
            if (_explorerFolder != folder)
                return;

            _listViewImageList.Images.Clear();
            _listViewImageList.Images.Add(UiUtils.ExtractPeIcon("shell32.dll", 0)!);
            _listViewImageList.Images.Add(UiUtils.ExtractPeIcon("shell32.dll", 4)!);

            lvwFiles.SelectedIndex = -1;
            lvwFiles.SetObjects(folder.Folders.Select(x => (object) new VirtualObject(x.Value, x.Key))
                .Concat(folder.Files.Select(x => (object) new VirtualObject(
                    _vspTree,
                    x,
                    (vobj, stream, platformId) => QueuedThumbnailer.Instance.LoadFromTexStream(
                        _listViewImageList.ImageSize.Width,
                        _listViewImageList.ImageSize.Height,
                        stream,
                        platformId
                    ).ContinueWith(img => {
                        if (!img.IsCompletedSuccessfully)
                            return (object?) null;

                        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                            return null;

                        if (!source.Contains(vobj))
                            return null;

                        _listViewImageList.Images.Add(img.Result);
                        img.Result.Dispose();

                        BeginInvoke(() => lvwFiles.RefreshObject(vobj));
                        return _listViewImageList.Images.Count - 1;
                    }, TaskScheduler.FromCurrentSynchronizationContext()))))
                .ToArray());
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    #endregion

    private class FolderTreeNode : TreeNode {
        public readonly VirtualFolder Folder;
        private bool _populateTriggered;

        public FolderTreeNode(VirtualFolder folder, string displayName, bool mayHaveChildren) {
            Text = displayName;
            Folder = folder;
            SelectedImageIndex = ImageIndex = 1;
            if (mayHaveChildren)
                Nodes.Add(new TreeNode(@"Expanding..."));
        }

        public bool CallerMustPopulate() {
            if (_populateTriggered)
                return false;

            _populateTriggered = true;
            return true;
        }

        public bool ShouldExpandRecursively() {
            if (Folder.Folders.Count == 1)
                return true;
            if (Folder.Folders.Count == 2 && Folder.Folders.Any(x => x.Value.Name.StartsWith('~')))
                return true;
            return false;
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
            if (column.AspectName == "Name") {
                _objects.Sort((a, b) => {
                    var r = a.IsFolder switch {
                        true when !b.IsFolder => -1,
                        false when b.IsFolder => 1,
                        _ => string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase)
                    };
                    return order switch {
                        SortOrder.None or SortOrder.Ascending => r,
                        SortOrder.Descending => -r,
                        _ => throw new InvalidOperationException(),
                    };
                });
            } else if (column.AspectName == "PackType") {
                _objects.Sort((a, b) => {
                    var r = a.IsFolder switch {
                        true when !b.IsFolder => -1,
                        true when b.IsFolder => 0,
                        false when b.IsFolder => 1,
                        _ => a.Lookup!.Type.CompareTo(b.Lookup!.Type),
                    };
                    return order switch {
                        SortOrder.None or SortOrder.Ascending => r,
                        SortOrder.Descending => -r,
                        _ => throw new InvalidOperationException(),
                    };
                });
            }
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
    private class VirtualObject : INotifyPropertyChanged {
        public readonly VirtualFile? File;
        public readonly VirtualFolder? Folder;

        private readonly string? _preferredName;
        
        private readonly Lazy<VirtualFileLookup>? _lookup;
        private readonly object _imageKeyFallback;
        private readonly Lazy<Task<object?>> _imageKeyTask;
        private readonly Lazy<string> _hash2;
        private readonly Lazy<long> _rawSize;
        private readonly Lazy<long> _storedSize;
        private readonly Lazy<long> _reservedSize;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file,
            Func<VirtualObject, Stream, PlatformId, Task<object?>> imageKeyGetter) {
            File = file;
            _lookup = new(() => tree.GetLookup(File));
            _imageKeyFallback = 0;
            _imageKeyTask = new(() => {
                Debug.Assert(Lookup != null);

                var canBeTexture = false;
                canBeTexture |= Lookup.Type == FileType.Texture;
                canBeTexture |= File.Name.EndsWith(".atex", StringComparison.InvariantCultureIgnoreCase);

                try {
                    // may be an .atex file
                    if (!canBeTexture && !File.NameResolved && Lookup.Type == FileType.Standard && Lookup.Size > 256)
                        canBeTexture = true;

                    if (!canBeTexture)
                        return Task.FromResult((object?) 0);

                    return imageKeyGetter(this, Lookup.CreateStream(), Lookup.DataStream.PlatformId);
                } catch (Exception e) {
                    Debug.WriteLine(e);
                }

                return Task.FromResult((object?) 0);
            });
            _hash2 = new(() => $"{Crc32.Get(File!.FullPath.Trim('/').ToLowerInvariant()):X08}");
            _rawSize = new(() => Lookup!.Size);
            _storedSize = new(() => Lookup!.OccupiedBlockBytes);
            _reservedSize = new(() => Lookup!.ReservedBlockBytes);
        }

        public VirtualObject(VirtualFolder folder, string? preferredName) {
            Folder = folder;
            _preferredName = preferredName;
            _imageKeyFallback = 1;
            _imageKeyTask = new(() => Task.FromResult((object?) 1));
            _hash2 = new("");
            _rawSize = _storedSize = _reservedSize = new(0L);
        }

        public bool IsFolder => _lookup is null;

        public VirtualFileLookup? Lookup => _lookup?.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        [UsedImplicitly] public string Name => _preferredName ?? (IsFolder ? Folder!.Name : File!.Name);

        [UsedImplicitly]
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

        [UsedImplicitly] public string Hash1 => $"{(IsFolder ? Folder!.FolderHash : File!.FileHash):X08}";

        [UsedImplicitly] public string Hash2 => _hash2.Value;

        [UsedImplicitly] public string RawSize => IsFolder ? "" : UiUtils.FormatSize(_rawSize.Value);

        [UsedImplicitly] public string StoredSize => IsFolder ? "" : UiUtils.FormatSize(_storedSize.Value);

        [UsedImplicitly] public string ReservedSize => IsFolder ? "" : UiUtils.FormatSize(_reservedSize.Value);

        [UsedImplicitly]
        public object Image => _imageKeyTask.Value.IsCompletedSuccessfully
            ? _imageKeyTask.Value.Result ?? _imageKeyFallback
            : _imageKeyFallback;

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
