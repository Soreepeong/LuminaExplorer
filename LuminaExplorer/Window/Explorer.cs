using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Structs;
using LuminaExplorer.AppControl;
using LuminaExplorer.LazySqPackTree;
using LuminaExplorer.LazySqPackTree.VirtualFileStream;
using LuminaExplorer.Util;

namespace LuminaExplorer.Window;

public partial class Explorer : Form {
    private readonly VirtualSqPackTree _vspTree;

    private readonly FileViewControl _fileViewControl;
    private readonly ImageList _smallImageList;
    private readonly ImageList _largeImageList;

    private VirtualFolder? _explorerFolder;

    public Explorer(VirtualSqPackTree vspTree) {
        _vspTree = vspTree;

        InitializeComponent();

        splSub.Panel2.Controls.Add(_fileViewControl = new() {
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Dock = DockStyle.Fill,
        });

        _largeImageList = new();
        _largeImageList.ColorDepth = ColorDepth.Depth32Bit;
        _largeImageList.ImageSize = new(64, 64);
        _largeImageList.Images.Add(Extract("shell32.dll", 0)!);
        _largeImageList.Images.Add(Extract("shell32.dll", 4)!);

        lvwFiles.LargeImageList = _largeImageList;
        lvwFiles.VirtualListDataSource = new ExplorerListViewDataSource(lvwFiles);
        lvwFiles.PrimarySortColumn = colFilesName;
        lvwFiles.PrimarySortOrder = SortOrder.Ascending;
        lvwFiles.View = View.LargeIcon;

        _smallImageList = new();
        _smallImageList.ColorDepth = ColorDepth.Depth32Bit;
        _smallImageList.Images.Add(Extract("shell32.dll", 0, false)!);
        _smallImageList.Images.Add(Extract("shell32.dll", 4, false)!);

        tvwFiles.ImageList = _smallImageList;
        tvwFiles.Nodes.Add(new FolderTreeNode(vspTree.RootFolder, @"(root)", true));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];

        TryNavigateTo("chara/monster/m0361/obj/body/b0001/texture");
    }

    #region Event Handlers

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
        if (e.Node is FolderTreeNode ln)
            ExpandFolderTreeNode(ln, true);
    }

    private void tvwFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode node)
            SetActiveExplorerFolder(node.Folder);
    }

    private void lvwFiles_DoubleClick(object sender, EventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;
        if (lvwFiles.SelectedIndices.Count == 0)
            return;
        if (source[lvwFiles.SelectedIndices[0]].Folder is { } folder)
            SetActiveExplorerFolder(folder);
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
                StreamContents = stream => _vspTree.GetLookup(x).DataStream.CopyTo(stream),
            }).ToArray());

            DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
        }
    }

    private void lvwFiles_KeyPress(object sender, KeyPressEventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;
        if (e.KeyChar == (char)Keys.Enter) {
            e.KeyChar = (char)Keys.Escape;
            // e.Handled = true;
            if (lvwFiles.SelectedIndices.Count == 0)
                return;
            if (source[lvwFiles.SelectedIndices[0]].Folder is { } folder)
                SetActiveExplorerFolder(folder);
        }
    }

    private void lvwFiles_SelectedIndexChanged(object sender, EventArgs e) {
        if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
            return;

        if (lvwFiles.SelectedIndices.Count is > 1 or 0) {
            _fileViewControl.ClearFile();
            return;
        }

        if (source[lvwFiles.SelectedIndices[0]].File is { } file) {
            var isFocused = lvwFiles.Focused;
            _fileViewControl.SetFile(_vspTree, file);
            if (isFocused)
                lvwFiles.Focus();
        }
    }

    #endregion

    private Task<FolderTreeNode> TryNavigateTo(params string[] pathComponents) =>
        TryNavigateToImpl(
            (FolderTreeNode)tvwFiles.Nodes[0],
            Path.Join(pathComponents).Replace('\\', '/').Split('/'),
            0);

    private Task<FolderTreeNode> TryNavigateToImpl(FolderTreeNode node, string[] parts, int partIndex) {
        for (; partIndex < parts.Length; partIndex++) {
            var name = parts[partIndex];
            if (name == ".")
                continue;

            if (name == "..") {
                node = node.Parent as FolderTreeNode ?? node;
                continue;
            }

            return ExpandFolderTreeNode(node).ContinueWith(_ => {
                var i = 0;
                for (; i < node.Nodes.Count; i++) {
                    if (node.Nodes[i] is FolderTreeNode subnode &&
                        string.Compare(subnode.Folder.Name, name, StringComparison.OrdinalIgnoreCase) == 0) {
                        return TryNavigateToImpl(subnode, parts, partIndex + 1);
                    }
                }

                SetActiveExplorerFolder(node.Folder);
                return Task.FromResult(node);
            }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        SetActiveExplorerFolder(node.Folder);
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
                            ((TreeNode)n).Expand();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return _vspTree.AsFoldersResolved(ln.Folder)
            .ContinueWith(_ => {
                ln.Nodes.Clear();
                ln.Nodes.AddRange(ln.Folder.Folders
                    .Where(x => x.Key != "..")
                    .OrderBy(x => x.Key.ToLowerInvariant())
                    .Select(x =>
                        (TreeNode)new FolderTreeNode(x.Value, x.Key,
                            !_vspTree.WillFolderNeverHaveSubfolders(x.Value)))
                    .ToArray());

                if (!neverExpandRecursively && ln.ShouldExpandRecursively()) {
                    foreach (var n in ln.Nodes)
                        ((TreeNode)n).Expand();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SetActiveExplorerFolder(VirtualFolder folder) {
        if (_explorerFolder == folder)
            return;

        _explorerFolder = folder;

        var resolveTask = _vspTree.AsFilesResolved(_explorerFolder);
        if (!resolveTask.IsCompleted)
            lvwFiles.SetObjects(Array.Empty<object>());

        resolveTask.ContinueWith(_ => {
            if (_explorerFolder != folder)
                return;

            while (_largeImageList.Images.Count > 2)
                _largeImageList.Images.RemoveAt(_largeImageList.Images.Count - 1);

            lvwFiles.SetObjects(folder.Folders.Select(x => (object)new VirtualObject(x.Value, x.Key))
                .Concat(folder.Files.Select(x => (object)new VirtualObject(
                    _vspTree,
                    x,
                    (vobj, tvfs) => QueuedThumbnailer.Instance.LoadFrom(_largeImageList.ImageSize.Width,
                        _largeImageList.ImageSize.Height,
                        tvfs
                    ).ContinueWith(img => {
                        if (!img.IsCompletedSuccessfully)
                            return (object?)null;

                        _largeImageList.Images.Add(img.Result);
                        img.Result.Dispose();

                        BeginInvoke(() => lvwFiles.RefreshObject(vobj));
                        return _largeImageList.Images.Count - 1;
                    }, TaskScheduler.FromCurrentSynchronizationContext()))))
                .ToArray());
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
                        _ => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
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
            _objects[index] = (VirtualObject)modelObject;

        public VirtualObject this[int n] => _objects[n];

        public IEnumerator<VirtualObject> GetEnumerator() => _objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _objects.GetEnumerator();

        public int Count => _objects.Count;
    }

    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
    private class VirtualObject : INotifyPropertyChanged {
        public readonly VirtualFile? File;
        public readonly VirtualFolder? Folder;

        private readonly Lazy<VirtualFileLookup>? _lookup;
        private readonly object _imageKeyFallback;
        private readonly Lazy<Task<object?>> _imageKeyTask;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file,
            Func<VirtualObject, TextureVirtualFileStream, Task<object?>> imageKeyGetter) {
            File = file;
            Name = file.Name;
            _lookup = new(() => tree.GetLookup(File));
            _imageKeyFallback = 0;
            _imageKeyTask = new(() => {
                if (Lookup!.Type != FileType.Texture &&
                    !File.Name.EndsWith(".atex", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult((object?)0);

                try {
                    if (Lookup?.DataStream is TextureVirtualFileStream tvfs)
                        return imageKeyGetter(this, tvfs);
                } catch (Exception e) {
                    Debug.WriteLine(e);
                }

                return Task.FromResult((object?)0);
            });
        }

        public VirtualObject(VirtualFolder folder, string? preferredName) {
            Folder = folder;
            Name = preferredName ?? folder.Name;
            _imageKeyFallback = 1;
            _imageKeyTask = new(() => Task.FromResult((object?)1));
        }

        public bool IsFolder => _lookup is null;

        public VirtualFileLookup? Lookup => _lookup?.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        [UsedImplicitly] public string Name { get; }

        [UsedImplicitly]
        public string PackTypeString => _lookup is null
            ? "(Folder)"
            : _lookup.Value.Type is var x
                ? x switch {
                    FileType.Empty => "Placeholder",
                    FileType.Standard => "Standard",
                    FileType.Model => "Model",
                    FileType.Texture => "Texture",
                    _ => $"{x}",
                }
                : "<error>";

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
