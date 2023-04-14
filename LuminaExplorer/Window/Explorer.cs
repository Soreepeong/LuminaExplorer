using System.Runtime.InteropServices;
using Lumina.Data;
using LuminaExplorer.AppControl;
using LuminaExplorer.LazySqPackTree;
using LuminaExplorer.Util;

namespace LuminaExplorer.Window;

public partial class Explorer : Form {
    private readonly VirtualSqPackTree _vspTree;

    private readonly FileViewControl _fileViewControl;
    private readonly ImageList _smallImageList;
    private readonly ImageList _largeImageList;

    private VirtualFolder? _explorerFolder;
    private List<VirtualObject>? _explorerObjects;

    public Explorer(VirtualSqPackTree vspTree) {
        _vspTree = vspTree;

        InitializeComponent();

        splSub.Panel2.Controls.Add(_fileViewControl = new() {
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Dock = DockStyle.Fill,
        });

        _smallImageList = new();
        _smallImageList.Images.Add(Extract("shell32.dll", 0, false)!);
        _smallImageList.Images.Add(Extract("shell32.dll", 4, false)!);

        _largeImageList = new();
        _largeImageList.Images.Add(Extract("shell32.dll", 0, true)!);
        _largeImageList.Images.Add(Extract("shell32.dll", 4, true)!);

        lvwFiles.SmallImageList = _smallImageList;
        lvwFiles.LargeImageList = _largeImageList;

        tvwFiles.ImageList = _smallImageList;
        tvwFiles.Nodes.Add(new FolderTreeNode(vspTree.RootFolder, @"(root)", true));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];
    }

    private void Explorer_FormClosed(object sender, FormClosedEventArgs e) {
        _smallImageList.Dispose();
        _largeImageList.Dispose();
    }

    private void tvwFiles_AfterExpand(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode ln) {
            if (ln.ShouldExpandRecursively()) {
                BeginInvoke(() => {
                    foreach (var n in e.Node.Nodes)
                        ((TreeNode)n).Expand();
                });
            }
        }
    }

    private void tvwFiles_BeforeExpand(object? sender, TreeViewCancelEventArgs e) {
        if (e.Node is FolderTreeNode ln) {
            if (ln.CallerMustPopulate()) {
                _vspTree.AsFoldersResolved(ln.Folder)
                    .ContinueWith(_ => {
                        ln.Nodes.Clear();
                        ln.Nodes.AddRange(ln.Folder.Folders
                            .Where(x => x.Key != "..")
                            .OrderBy(x => x.Value.Name.ToLowerInvariant())
                            .Select(x =>
                                (TreeNode)new FolderTreeNode(x.Value, x.Key, !_vspTree.WillFolderNeverHaveSubfolders(x.Value)))
                            .ToArray());

                        if (ln.ShouldExpandRecursively()) {
                            foreach (var n in ln.Nodes)
                                ((TreeNode)n).Expand();
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }
    }

    private void tvwFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode node)
            SetActiveExplorerFolder(node.Folder);
    }

    private void lvwFiles_DoubleClick(object sender, EventArgs e) {
        if (lvwFiles.SelectedIndices.Count == 0 || _explorerObjects is null)
            return;
        if (_explorerObjects[lvwFiles.SelectedIndices[0]].Folder is { } folder)
            SetActiveExplorerFolder(folder);
    }

    private void lvwFiles_ItemDrag(object sender, ItemDragEventArgs e) {
        if (_explorerObjects is null)
            return;

        var folders = new List<VirtualFolder>();
        var files = new List<VirtualFile>();
        for (var i = 0; i < lvwFiles.SelectedIndices.Count; i++) {
            var obj = _explorerObjects[lvwFiles.SelectedIndices[i]];
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
                StreamContents = stream => _vspTree.GetLookup(x).DataStream.CopyTo(stream),
            }).ToArray());

            DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
        }
    }

    private void lvwFiles_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e) {
        if (_explorerObjects is null)
            return;
        for (var i = e.StartIndex; i <= e.EndIndex; i++)
            _ = _explorerObjects[i].ListViewItem;
    }

    private void lvwFiles_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e) {
        if (_explorerObjects is null)
            e.Item = new("Expanding...");
        else
            e.Item = _explorerObjects[e.ItemIndex].ListViewItem;
    }

    private void lvwFiles_SearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e) {
        if (e is { IsTextSearch: true, Text: { } } && _explorerObjects is not null) {
            for (var i = e.StartIndex; i < _explorerObjects.Count; i++) {
                if (_explorerObjects[i].Name.StartsWith(e.Text, StringComparison.InvariantCultureIgnoreCase)) {
                    e.Index = i;
                    return;
                }
            }

            for (var i = 0; i < e.StartIndex; i++) {
                if (_explorerObjects[i].Name.StartsWith(e.Text, StringComparison.InvariantCultureIgnoreCase)) {
                    e.Index = i;
                    return;
                }
            }
        }
    }

    private void lvwFiles_SelectedIndexChanged(object sender, EventArgs e) {
        if (lvwFiles.SelectedIndices.Count is > 1 or 0 || _explorerObjects is null) {
            _fileViewControl.SetFile(null, null);
            return;
        }

        if (_explorerObjects[lvwFiles.SelectedIndices[0]].File is { } file) {
            var isFocused = lvwFiles.Focused;
            _fileViewControl.SetFile(_vspTree, file);
            if (isFocused)
                lvwFiles.Focus();
        }
    }

    private void SetActiveExplorerFolder(VirtualFolder newFolder) {
        if (_explorerFolder == newFolder)
            return;

        _explorerFolder = newFolder;

        var resolveTask = _vspTree.AsFilesResolved(_explorerFolder);
        if (!resolveTask.IsCompleted) {
            lvwFiles.BeginUpdate();
            _explorerObjects = null;
            lvwFiles.VirtualListSize = 1;
            lvwFiles.EndUpdate();
        }

        resolveTask.ContinueWith(_ => {
            if (_explorerFolder != newFolder)
                return;

            lvwFiles.BeginUpdate();
            _explorerObjects = new();
            _explorerObjects.AddRange(_explorerFolder.Folders.Values
                .OrderBy(x => x.Name.ToLowerInvariant())
                .Select(x => new VirtualObject {
                    Folder = x
                }));
            _explorerObjects.AddRange(_explorerFolder.Files
                .OrderBy(x => x.Name.ToLowerInvariant())
                .Select(x => new VirtualObject {
                    File = x
                }));
            lvwFiles.VirtualListSize = _explorerObjects.Count;
            lvwFiles.EndUpdate();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

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

    private class VirtualObject {
        public VirtualFile? File;
        public VirtualFolder? Folder;

        private ListViewItem? _lvi;

        public string Name => File?.Name ?? Folder?.Name ?? throw new InvalidOperationException();

        public ListViewItem ListViewItem =>
            _lvi ??= new(new ListViewItem.ListViewSubItem[] {
                new() {
                    Name = @"Name",
                    Text = Name,
                }
            }, File is null ? 1 : 0);
    }

    public static Icon? Extract(string filePath, int index, bool largeIcon = true) {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        nint hIcon;
        if (largeIcon)
            ExtractIconEx(filePath, index, out hIcon, IntPtr.Zero, 1);
        else
            ExtractIconEx(filePath, index, IntPtr.Zero, out hIcon, 1);

        return hIcon != IntPtr.Zero ? Icon.FromHandle(hIcon) : null;
    }

    [DllImport("shell32", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, IntPtr phiconSmall,
        int nIcons);

    [DllImport("shell32", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr phiconLarge, out IntPtr phiconSmall,
        int nIcons);
}
