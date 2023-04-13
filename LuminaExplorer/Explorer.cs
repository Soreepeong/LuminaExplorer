using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Be.Windows.Forms;
using Lumina;
using Lumina.Data;
using LuminaExplorer.LazySqPackTree;
using LuminaExplorer.Util;

namespace LuminaExplorer;

public partial class Explorer : Form {
    private VirtualFolder? _explorerFolder;
    private readonly ImageList _smallImageList;
    private readonly ImageList _largeImageList;

    public Explorer(HashDatabase hashDatabase, GameData gameData) {
        InitializeComponent();

        _smallImageList = new();
        _smallImageList.Images.Add(Extract("shell32.dll", 0, false)!);
        _smallImageList.Images.Add(Extract("shell32.dll", 4, false)!);

        _largeImageList = new();
        _largeImageList.Images.Add(Extract("shell32.dll", 0, true)!);
        _largeImageList.Images.Add(Extract("shell32.dll", 4, true)!);

        lvwFiles.SmallImageList = _smallImageList;
        lvwFiles.LargeImageList = _largeImageList;

        tvwFiles.ImageList = _smallImageList;
        tvwFiles.Nodes.Add(new LazyNode(hashDatabase, gameData));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];
    }

    private void Explorer_FormClosed(object sender, FormClosedEventArgs e) {
        _smallImageList.Dispose();
        _largeImageList.Dispose();
    }

    private void tvwFiles_BeforeExpand(object? sender, TreeViewCancelEventArgs e) {
        if (e.Node is LazyNode ln) {
            ln.Populate(this);
            if (ln.ShouldExpandRecursively()) {
                BeginInvoke(() => {
                    foreach (var n in e.Node.Nodes)
                        ((TreeNode)n).Expand();
                });
            }
        }
    }

    private void tvwFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        if (e.Node is LazyNode node)
            SetActiveExplorerFolder(node.Folder);
    }

    private void lvwFiles_DoubleClick(object sender, EventArgs e) {
        if (lvwFiles.SelectedItems.Count == 0)
            return;
        if (lvwFiles.SelectedItems[0] is FolderListViewItem f)
            SetActiveExplorerFolder(f.Folder);
    }

    private void lvwFiles_ItemDrag(object sender, ItemDragEventArgs e) {
        var folders = new List<VirtualFolder>();
        var files = new List<VirtualFile>();
        foreach (var sel in lvwFiles.SelectedItems) {
            switch (sel) {
                case FolderListViewItem f1:
                    folders.Add(f1.Folder);
                    break;
                case FileListViewItem f2:
                    files.Add(f2.File);
                    break;
            }
        }

        // TODO: export using IStorage, and maybe offer concrete file contents so that it's possible to drag into external hex editors?
        // https://devblogs.microsoft.com/oldnewthing/20080320-00/?p=23063
        // https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-istorage

        if (files.Any()) {
            var virtualFileDataObject = new VirtualFileDataObject();

            // Provide a virtual file (generated on demand) containing the letters 'a'-'z'
            virtualFileDataObject.SetData(files.Select(x => new VirtualFileDataObject.FileDescriptor {
                Name = x.Name,
                Length = x.Metadata.RawFileSize,
                ChangeTimeUtc = x.Owner.DatFiles[x.DataFileId].File.LastWriteTimeUtc,
                StreamContents = stream => stream.Write(x.Owner.GetFile<FileResource>(x.DataFileId, x.Offset).Data)
            }).ToArray());

            DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
        }
    }

    private void lvwFiles_SelectedIndexChanged(object sender, EventArgs e) {
        if (lvwFiles.SelectedItems.Count != 1 || lvwFiles.SelectedItems[0] is not FileListViewItem item) {
            splSub.Panel2.Controls.Clear();
            return;
        }

        byte[] data;

        try {
            data = item.File.GetFile().Data;
        } catch (FileNotFoundException) {
            splSub.Panel2.Controls.Clear();
            return;
        }

        var hexbox = new HexBox {
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Dock = DockStyle.Fill,
            Font = new(FontFamily.GenericMonospace, 12),
            VScrollBarVisible = true,
            ColumnInfoVisible = true,
            GroupSeparatorVisible = true,
            LineInfoVisible = true,
            StringViewVisible = true,
            ByteProvider = new DynamicByteProvider(data),
            ReadOnly = true,
        };
        splSub.Panel2.Controls.Add(hexbox);
        lvwFiles.Focus();
    }

    private void SetActiveExplorerFolder(VirtualFolder newFolder) {
        if (_explorerFolder == newFolder)
            return;

        _explorerFolder = newFolder;

        lvwFiles.Items.Clear();
        if (!_explorerFolder.IsFolderResolved())
            lvwFiles.Items.Add("Expanding...");

        _explorerFolder.ResolveFiles(folder => BeginInvoke(() => {
            if (_explorerFolder != folder)
                return;

            lvwFiles.Items.Clear();
            lvwFiles.Items.AddRange(_explorerFolder.Folders.Values
                .OrderBy(x => x.Name.ToLowerInvariant())
                .Select(x => (ListViewItem)new FolderListViewItem(x))
                .Concat(_explorerFolder.Files
                    .OrderBy(x => x.Name.ToLowerInvariant())
                    .Select(x => (ListViewItem)new FileListViewItem(x)))
                .ToArray());
        }));
    }

    private class LazyNode : TreeNode {
        public readonly VirtualFolder Folder;
        private bool _populateTriggered;

        public LazyNode(HashDatabase hashDatabase, GameData gameData) {
            Text = @"(root)";
            Folder = VirtualFolder.CreateRoot(hashDatabase, gameData);
            SelectedImageIndex = ImageIndex = 1;
            Nodes.Add(new TreeNode(@"Expanding..."));
        }

        private LazyNode(VirtualFolder folder) {
            Text = folder.Name;
            Folder = folder;
            SelectedImageIndex = ImageIndex = 1;
            if (folder.Folders.Any() || !Folder.IsFolderResolved())
                Nodes.Add(new TreeNode(@"Expanding..."));
        }

        public void Populate(Control context) {
            if (_populateTriggered)
                return;

            _populateTriggered = true;
            Folder.ResolveFolders(_ => context.BeginInvoke(() => {
                Nodes.Clear();
                Nodes.AddRange(Folder.Folders.Values.OrderBy(x => x.Name.ToLowerInvariant())
                    .Select(x => (TreeNode)new LazyNode(x))
                    .ToArray());

                if (ShouldExpandRecursively()) {
                    foreach (var n in Nodes)
                        ((TreeNode)n).Expand();
                }
            }));
        }

        public bool ShouldExpandRecursively() {
            if (Folder.Folders.Count == 1)
                return true;
            if (Folder.Folders.Count == 2 && Folder.Folders.Any(x => x.Value.NameUnknown))
                return true;
            return false;
        }
    }

    private class FileNode : TreeNode {
        public readonly VirtualFile File;

        public FileNode(VirtualFile file) {
            File = file;
            Text = file.Name;
            SelectedImageIndex = ImageIndex = 0;
        }
    }

    private class FolderListViewItem : ListViewItem {
        public readonly VirtualFolder Folder;

        public FolderListViewItem(VirtualFolder folder) : base(folder.Name) {
            Folder = folder;
            ImageIndex = 1;
        }
    }

    private class FileListViewItem : ListViewItem {
        public readonly VirtualFile File;

        public FileListViewItem(VirtualFile file) : base(file.Name) {
            File = file;
            ImageIndex = 0;
        }
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
