using System.Collections;
using BrightIdeasSoftware;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class FileListHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly VirtualObjectListView _listView;
        private readonly ComboBox _cboView;

        private ExplorerListViewDataSource? _source;
        private VirtualSqPackTree? _tree;

        public FileListHandler(Explorer explorer) {
            _explorer = explorer;
            _listView = explorer.lvwFiles;
            _cboView = explorer.cboView.ComboBox!;

            _cboView.SelectedIndex = _cboView.Items.Count - 1; // detail view
            _cboView.SelectedIndexChanged += cboView_SelectedIndexChanged;

            _listView.SmallImageList = new();
            _listView.SmallImageList.ColorDepth = ColorDepth.Depth32Bit;
            _listView.SmallImageList.ImageSize = new(16, 16);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
                _listView.SmallImageList.Images.Add(icon);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                _listView.SmallImageList.Images.Add(icon);

            _listView.LargeImageList = new();
            _listView.LargeImageList.ColorDepth = ColorDepth.Depth32Bit;
            _listView.LargeImageList.ImageSize = new(32, 32);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, true)!)
                _listView.LargeImageList.Images.Add(icon);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, true)!)
                _listView.LargeImageList.Images.Add(icon);

            if (_explorer.Tree is { } tree)
                _listView.VirtualListDataSource = _source = new(_listView, tree);
            else
                _listView.VirtualListDataSource = new AbstractVirtualListDataSource(_listView);
            _listView.PrimarySortColumn = _explorer.colFilesName;
            _listView.PrimarySortOrder = SortOrder.Ascending;

            _listView.SelectionChanged += SelectionChanged;
            _listView.ItemDrag += ItemDrag;
            _listView.DoubleClick += DoubleClick;
            _listView.KeyPress += KeyPress;
            _listView.KeyUp += KeyUp;
            _listView.MouseUp += MouseUp;
            _listView.MouseWheel += MouseWheel;

            if (_tree is not null) {
                _tree.FolderChanged += VirtualFolderChanged;
                _tree.FileChanged += VirtualFileChanged;
            }
        }

        public VirtualSqPackTree? Tree {
            get => _tree;
            set {
                if (_tree == value)
                    return;

                if (_tree is not null) {
                    _tree.FolderChanged -= VirtualFolderChanged;
                    _tree.FileChanged -= VirtualFileChanged;
                    _source?.Dispose();
                    _source = null;
                    // cannot set to null, so replace it with an empty data source.
                    _listView.VirtualListDataSource = new AbstractVirtualListDataSource(_listView);
                }

                _tree = value;

                if (_tree is not null) {
                    _tree.FolderChanged += VirtualFolderChanged;
                    _tree.FileChanged += VirtualFileChanged;
                    _listView.VirtualListDataSource = _source = new(_listView, _tree);
                }
            }
        }

        public VirtualFolder? CurrentFolder {
            get => _source?.CurrentFolder;
            set {
                if (_source is not null)
                    _source.CurrentFolder = value;

                _explorer.colFilesFullPath.IsVisible = value is null;
            }
        }

        public int ItemCount => _source?.Count ?? 0;

        public void Dispose() {
            Tree = null;

            _listView.SelectionChanged -= SelectionChanged;
            _listView.ItemDrag -= ItemDrag;
            _listView.DoubleClick -= DoubleClick;
            _listView.KeyPress -= KeyPress;
            _listView.KeyUp -= KeyUp;
            _listView.MouseUp -= MouseUp;
            _listView.MouseWheel -= MouseWheel;
            _cboView.SelectedIndexChanged -= cboView_SelectedIndexChanged;
        }

        public void Focus() => _listView.Focus();

        public void Clear() {
            if (_source is not { } source)
                return;

            source.CurrentFolder = null;
            _listView.ClearObjects();
        }

        public void AddObjects(ICollection objects) => _listView.AddObjects(objects);

        private void VirtualFileChanged(VirtualFile changedFile) {
            if (_tree is not { } tree || _source is not { } source)
                return;

            if (source.CurrentFolder != changedFile.Parent)
                return;

            if (source.FirstOrDefault(x => x.File == changedFile) is { } modelObject) {
                modelObject.Name = changedFile.Name;
                modelObject.FullPath = tree.GetFullPath(changedFile);
            }
        }

        private void VirtualFolderChanged(VirtualFolder changedFolder,
            VirtualFolder[]? previousPathFromRoot) {
            if (_tree is not { } tree || _source is not { } source)
                return;

            if (source.CurrentFolder != changedFolder.Parent)
                return;

            if (source.FirstOrDefault(x => x.Folder == changedFolder) is { } modelObject) {
                modelObject.Name = changedFolder.Name;
                modelObject.FullPath = tree.GetFullPath(changedFolder);
            }
        }

        private void cboView_SelectedIndexChanged(object? sender, EventArgs e) {
            _listView.View = _cboView.SelectedIndex switch {
                <= 6 => View.LargeIcon,
                7 => View.SmallIcon,
                8 => View.List,
                9 => View.Details,
                _ => throw new FailFastException("cboView.SelectedIndex >= cboView.SelectedIndex.Items.Count?"),
            };

            if (_listView.VirtualListDataSource is ExplorerListViewDataSource source) {
                source.ImageThumbnailSize = _cboView.SelectedIndex switch {
                    <= 6 => 256 - 32 * _cboView.SelectedIndex,
                    _ => 0,
                };
            }
        }

        private void MouseUp(object? sender, MouseEventArgs e) {
            switch (e.Button) {
                case MouseButtons.XButton1:
                    _explorer._navigationHandler?.NavigateBack();
                    break;
                case MouseButtons.XButton2:
                    _explorer._navigationHandler?.NavigateForward();
                    break;
            }
        }

        private void MouseWheel(object? sender, MouseEventArgs e) {
            if (ModifierKeys == Keys.Control) {
                _cboView.SelectedIndex = e.Delta switch {
                    > 0 => Math.Max(0, _cboView.SelectedIndex - 1),
                    < 0 => Math.Min(_cboView.Items.Count - 1, _cboView.SelectedIndex + 1),
                    _ => _cboView.SelectedIndex
                };
            }
        }

        private void DoubleClick(object? sender, EventArgs e) {
            if (_listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return;
            if (_listView.SelectedIndices.Count == 0)
                return;

            var vo = source[_listView.SelectedIndices[0]];
            if (vo.IsFolder)
                _explorer._navigationHandler?.NavigateTo(vo.Folder, true);
        }

        private void ItemDrag(object? sender, ItemDragEventArgs e) {
            if (Tree is not { } tree)
                return;

            // TODO: export using IStorage, and maybe offer concrete file contents so that it's possible to drag into external hex editors?
            // https://devblogs.microsoft.com/oldnewthing/20080320-00/?p=23063
            // https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-istorage

            var files = GetSelectedFiles();
            if (files.Any()) {
                var virtualFileDataObject = new VirtualFileDataObject();

                // Provide a virtual file (generated on demand) containing the letters 'a'-'z'
                virtualFileDataObject.SetData(files.Select(x => new VirtualFileDataObject.FileDescriptor {
                    Name = x.Name,
                    Length = tree.GetLookup(x).Size,
                    StreamContents = dstStream => {
                        using var srcStream = tree.GetLookup(x).CreateStream();
                        srcStream.CopyTo(dstStream);
                    },
                }).ToArray());

                _explorer.DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
            }
        }

        private void KeyPress(object? sender, KeyPressEventArgs e) {
            if (_listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return;
            if (e.KeyChar == (char) Keys.Enter) {
                if (_listView.SelectedIndices.Count == 0)
                    return;
                if (source[_listView.SelectedIndices[0]].Folder is { } folder)
                    _explorer._navigationHandler?.NavigateTo(folder, true);
            }
        }

        private void KeyUp(object? sender, KeyEventArgs e) {
            switch (e.KeyCode) {
                case Keys.Left when e is {Control: false, Alt: true, Shift: false}:
                case Keys.Back when e is {Control: false, Alt: false, Shift: false}:
                case Keys.BrowserBack:
                    _explorer._navigationHandler?.NavigateBack();
                    break;
                case Keys.Right when e is {Control: false, Alt: true, Shift: false}:
                case Keys.BrowserForward:
                    _explorer._navigationHandler?.NavigateForward();
                    break;
                case Keys.Up when e is {Control: false, Alt: true, Shift: false}:
                    _explorer._navigationHandler?.NavigateUp();
                    break;
            }
        }

        private void SelectionChanged(object? sender, EventArgs e) {
            if (_explorer._previewHandler is not { } previewHandler || _source is not { } source)
                return;

            previewHandler.ClearPreview();

            if (_listView.SelectedIndices.Count is > 1 or 0)
                return;

            var vo = source[_listView.SelectedIndices[0]];
            if (!vo.IsFolder)
                previewHandler.PreviewFile(vo.File);
        }

        // ReSharper disable once UnusedMember.Local
        public List<VirtualFolder> GetSelectedFolders() {
            var folders = new List<VirtualFolder>();
            if (_listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return folders;

            for (var i = 0; i < _listView.SelectedIndices.Count; i++) {
                var obj = source[_listView.SelectedIndices[i]];
                if (obj.IsFolder)
                    folders.Add(obj.Folder);
            }

            return folders;
        }

        public List<VirtualFile> GetSelectedFiles() {
            var folders = new List<VirtualFile>();
            if (_listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return folders;

            for (var i = 0; i < _listView.SelectedIndices.Count; i++) {
                var obj = source[_listView.SelectedIndices[i]];
                if (!obj.IsFolder)
                    folders.Add(obj.File);
            }

            return folders;
        }
    }
}
