using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BrightIdeasSoftware;
using LuminaExplorer.App.Thumbnails;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class FileListHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly VirtualObjectListView _listView;
        private readonly ComboBox _cboView;

        private readonly ThumbnailDecoration _thumbnailDecoration;
        private readonly Icon? _folderIconLarge;
        private readonly Icon? _fileIconLarge;
        private readonly Bitmap? _fileImageSmall;
        private readonly Bitmap? _fileImageLarge;
        private readonly FileIconBadge _fileIconBadge;
        private readonly ContextMenuStrip _contextMenu;

        private ExplorerListViewDataSource? _source;
        private IVirtualFileSystem? _vfs;
        private AppConfig _appConfig;

        public FileListHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this._vfs = explorer.Vfs;
            this._listView = explorer.lvwFiles;
            this._cboView = explorer.cboView.ComboBox!;
            this._appConfig = explorer.AppConfig;

            this._listView.SmallImageList = new();
            this._listView.SmallImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._listView.SmallImageList.ImageSize = this._listView.LogicalToDeviceUnits(new Size(16, 16));
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
                this._listView.SmallImageList.Images.Add(icon);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                this._listView.SmallImageList.Images.Add(icon);

            this._listView.LargeImageList = new();
            this._listView.LargeImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._listView.LargeImageList.ImageSize = this._listView.LogicalToDeviceUnits(new Size(32, 32));
            this._fileIconLarge = UiUtils.ExtractPeIcon("shell32.dll", 0, true);
            this._folderIconLarge = UiUtils.ExtractPeIcon("shell32.dll", 4, true);
            this._fileImageSmall = this._listView.SmallImageList.Images[0] as Bitmap;
            this._fileImageLarge = this._fileIconLarge?.ToBitmap();
            this._fileIconBadge = new(
                size => size <= this._listView.SmallImageList.ImageSize.Width
                    ? this._fileImageSmall
                    : this._fileImageLarge);

            if (this._vfs is { } tree) {
                this._listView.VirtualListDataSource = this._source = new(
                    this._listView,
                    tree,
                    this._appConfig.PreviewThumbnailerThreads) {
                    SortThreads = this._appConfig.SortThreads,
                };
            } else {
                this._listView.VirtualListDataSource = new AbstractVirtualListDataSource(this._listView);
            }

            // Shows model names next to folder names; sorting and other uses keep the plain name.
            this._explorer.colFilesName.AspectGetter = x => (x as VirtualObject)?.DisplayName;
            if (this._source is not null)
                this._source.FolderDisplayNameResolver = this._explorer.GetFolderDisplayName;

            this._listView.PrimarySortColumn = this._explorer.colFilesName;
            this._listView.PrimarySortOrder = SortOrder.Ascending;
            this._thumbnailDecoration = new(this);

            this._listView.SelectionChanged += this.SelectionChanged;
            this._listView.ItemDrag += this.ItemDrag;
            this._listView.DoubleClick += this.DoubleClick;
            this._listView.KeyDown += this.KeyDown;
            this._listView.KeyUp += this.KeyUp;
            this._listView.MouseUp += this.MouseUp;
            this._listView.MouseWheel += this.MouseWheel;
            this._listView.FormatRow += this.FormatRow;

            // Ctrl+C copies the selected files instead of the text of the rows.
            this._listView.CopySelectionOnControlC = false;
            this._contextMenu = new();
            this._contextMenu.Opening += this.ContextMenuOpening;
            this._listView.ContextMenuStrip = this._contextMenu;

            this._explorer.Resize += this.WindowResized;

            if (this._vfs is not null) {
                this._vfs.FolderChanged += this.IVirtualFolderChanged;
                this._vfs.FileChanged += this.IVirtualFileChanged;
            }

            this._cboView.SelectedIndex = this._appConfig.ListViewMode;
            this._cboView.SelectedIndexChanged += this.cboView_SelectedIndexChanged;
        }

        public IVirtualFileSystem? Vfs {
            get => this._vfs;
            set {
                if (this._vfs == value)
                    return;

                if (this._vfs is not null) {
                    this._vfs.FolderChanged -= this.IVirtualFolderChanged;
                    this._vfs.FileChanged -= this.IVirtualFileChanged;
                    SafeDispose.One(ref this._source);
                    // cannot set to null, so replace it with an empty data source.
                    this._listView.VirtualListDataSource = new AbstractVirtualListDataSource(this._listView);
                }

                this._vfs = value;

                if (this._vfs is not null) {
                    this._vfs.FolderChanged += this.IVirtualFolderChanged;
                    this._vfs.FileChanged += this.IVirtualFileChanged;
                    this._listView.VirtualListDataSource = this._source =
                        new(this._listView, this._vfs, this._appConfig.PreviewThumbnailerThreads) {
                            SortThreads = this._appConfig.SortThreads,
                        };
                }
            }
        }

        public AppConfig AppConfig {
            get => this._appConfig;
            set {
                if (this._appConfig == value)
                    return;

                this._appConfig = value;
                if (this._source is not null) {
                    this._source.PreviewCropThresholdAspectRatioRatio = value.CropThresholdAspectRatioRatio;
                    this._source.PreviewInterpolationMode = value.PreviewInterpolationMode;
                    this._source.PreviewThreads = value.PreviewThumbnailerThreads;
                    this._source.SortThreads = value.SortThreads;

                    this._cboView.SelectedIndex = value.ListViewMode;
                    this._listView.View = value.ListViewMode switch {
                        <= 7 => View.LargeIcon,
                        8 => View.SmallIcon,
                        9 => View.List,
                        10 => View.Details,
                        _ => throw new FailFastException(
                            "cboView.SelectedIndex >= cboView.SelectedIndex.Items.Count?"),
                    };

                    // 7 = LargeIcon(32px) but no thumbnails.

                    if (this._listView.VirtualListDataSource is ExplorerListViewDataSource source) {
                        source.ImageThumbnailSize = value.ListViewMode switch {
                            <= 6 => 256 - 32 * value.ListViewMode,
                            _ => 0,
                        };
                    }

                    this.RecalculateNumberOfPreviewsToCache();
                }
            }
        }

        public IVirtualFolder? CurrentFolder {
            get => this._source?.CurrentFolder;
            set {
                // The selection is dropped without a selection change event, so stop previewing the previous file
                // (including any SCD preview holding the audio device).
                if (!Equals(this._source?.CurrentFolder, value))
                    this._explorer._previewHandler?.ClearPreview();

                if (this._source is not null) this._source.CurrentFolder = value;

                this._explorer.colFilesFullPath.IsVisible = value is null;
                if (value is not null) this._explorer._searchHandler?.SearchAbort();
            }
        }

        public int ItemCount => this._source?.Count ?? 0;

        public void SelectFileAfterLoad(string name) => this._source?.SelectFileAfterLoad(name);

        public void RefreshFolderDisplayNames()
        {
            this._source?.RefreshFolderDisplayNames();
            this._listView.Invalidate();
        }

        public void Dispose()
        {
            this.Vfs = null;

            this._listView.SelectionChanged -= this.SelectionChanged;
            this._listView.ItemDrag -= this.ItemDrag;
            this._listView.DoubleClick -= this.DoubleClick;
            this._listView.KeyDown -= this.KeyDown;
            this._listView.KeyUp -= this.KeyUp;
            this._listView.MouseUp -= this.MouseUp;
            this._listView.MouseWheel -= this.MouseWheel;
            this._listView.FormatRow -= this.FormatRow;
            this._cboView.SelectedIndexChanged -= this.cboView_SelectedIndexChanged;
            this._listView.ContextMenuStrip = null;
            this._contextMenu.Opening -= this.ContextMenuOpening;
            this._contextMenu.Dispose();

            this._explorer.Resize -= this.WindowResized;

            this._folderIconLarge?.Dispose();
            this._fileIconLarge?.Dispose();
            this._fileImageSmall?.Dispose();
            this._fileImageLarge?.Dispose();
            this._fileIconBadge.Dispose();
        }

        public void Focus() => this._listView.Focus();

        public void Clear()
        {
            if (this._source is not { } source)
                return;

            source.CurrentFolder = null;
            this._listView.SetObjects(Array.Empty<object>());
        }

        public void AddObjects(ICollection objects) => this._listView.AddObjects(objects);

        private void IVirtualFileChanged(IVirtualFile changedFile)
        {
            if (this._vfs is not { } tree || this._source is not { } source)
                return;

            if (!Equals(source.CurrentFolder, changedFile.Parent))
                return;

            if (source.FirstOrDefault(x => Equals(x.File, changedFile)) is { } modelObject) {
                modelObject.Name = changedFile.Name;
                modelObject.FullPath = tree.GetFullPath(changedFile);
            }
        }

        private void IVirtualFolderChanged(
            IVirtualFolder changedFolder,
            IVirtualFolder[]? previousPathFromRoot)
        {
            if (this._vfs is not { } tree || this._source is not { } source)
                return;

            if (!Equals(source.CurrentFolder, changedFolder.Parent))
                return;

            if (source.FirstOrDefault(x => Equals(x.Folder, changedFolder)) is { } modelObject) {
                modelObject.Name = changedFolder.Name;
                modelObject.FullPath = tree.GetFullPath(changedFolder);
            }
        }

        private void cboView_SelectedIndexChanged(object? sender, EventArgs e) => this._explorer.AppConfig =
            this._appConfig with { ListViewMode = this._cboView.SelectedIndex };

        private void MouseUp(object? sender, MouseEventArgs e)
        {
            switch (e.Button) {
                case MouseButtons.XButton1:
                    this._explorer._navigationHandler?.NavigateBack();
                    break;
                case MouseButtons.XButton2:
                    this._explorer._navigationHandler?.NavigateForward();
                    break;
            }
        }

        private void MouseWheel(object? sender, MouseEventArgs e)
        {
            if (ModifierKeys == Keys.Control) {
                this._cboView.SelectedIndex = e.Delta switch {
                    > 0 => Math.Max(0, this._cboView.SelectedIndex - 1),
                    < 0 => Math.Min(this._cboView.Items.Count - 1, this._cboView.SelectedIndex + 1),
                    _ => this._cboView.SelectedIndex,
                };
            }
        }

        private void DoubleClick(object? sender, EventArgs e)
        {
            this.ExecuteItems(this.GetSelectedFiles(), this.GetSelectedFolders());
        }

        private void FormatRow(object? sender, FormatRowEventArgs e)
        {
            e.Item.Decoration = this._thumbnailDecoration;
        }

        private void ItemDrag(object? sender, ItemDragEventArgs e)
        {
            if (this.Vfs is not { } tree)
                return;

            // TODO: export using IStorage, and maybe offer concrete file contents so that it's possible to drag into external hex editors?
            // https://devblogs.microsoft.com/oldnewthing/20080320-00/?p=23063
            // https://learn.microsoft.com/en-us/windows/win32/api/objidl/nn-objidl-istorage

            var files = this.GetSelectedFiles();
            if (files.Any()) {
                var virtualFileDataObject = new VirtualFileDataObject();

                // Provide a virtual file (generated on demand) containing the letters 'a'-'z'
                virtualFileDataObject.SetData(
                    files.Select(
                        x => {
                            using var lookup = tree.GetLookup(x);
                            return new VirtualFileDataObject.FileDescriptor {
                                Name = x.Name,
                                Length = lookup.Size,
                                StreamContents = dstStream => {
                                    var innerLookup = tree.GetLookup(x);
                                    using var srcStream = innerLookup.CreateStream();
                                    srcStream.CopyTo(dstStream);
                                },
                            };
                        }).ToArray());

                this._explorer.DoDragDrop(virtualFileDataObject, DragDropEffects.Copy);
            }
        }

        private void KeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyData) {
                case Keys.Enter:
                    this.ExecuteItems(this.GetSelectedFiles(), this.GetSelectedFolders());
                    break;
                case Keys.Control | Keys.C:
                    if (this.CreateSelectionTarget() is { } target)
                        this._explorer.CopyItems(target, FileExportMode.AsIs);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
                case Keys.Control | Keys.Shift | Keys.C:
                    if (this.CreateSelectionTarget() is { } target2)
                        CopyPaths(target2);
                    e.Handled = e.SuppressKeyPress = true;
                    break;
            }
        }

        private void ContextMenuOpening(object? sender, CancelEventArgs e)
        {
            ClearContextMenu(this._contextMenu);
            if (this.CreateSelectionTarget() is not { } target) {
                e.Cancel = true;
                return;
            }

            this._explorer.PopulateFileContextMenu(this._contextMenu.Items, target);

            // Opening is raised already cancelled if the menu had no items.
            e.Cancel = false;
        }

        /// <summary>Creates the target of file operations from the selected items.</summary>
        private FileOperationTarget? CreateSelectionTarget()
        {
            if (this._vfs is not { } tree)
                return null;

            var files = this.GetSelectedFiles();
            var folders = this.GetSelectedFolders();
            if (files.Count == 0 && folders.Count == 0)
                return null;

            return FileOperationTarget.FromItems(
                tree,
                files,
                folders,
                files.Count > 0 || folders.Count == 1 ? () => this.ExecuteItems(files, folders) : null);
        }

        private void KeyUp(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode) {
                case Keys.Left when e is { Control: false, Alt: true, Shift: false }:
                case Keys.Back when e is { Control: false, Alt: false, Shift: false }:
                case Keys.BrowserBack:
                    this._explorer._navigationHandler?.NavigateBack();
                    break;
                case Keys.Right when e is { Control: false, Alt: true, Shift: false }:
                case Keys.BrowserForward:
                    this._explorer._navigationHandler?.NavigateForward();
                    break;
                case Keys.Up when e is { Control: false, Alt: true, Shift: false }:
                    this._explorer._navigationHandler?.NavigateUp();
                    break;
            }
        }

        private void SelectionChanged(object? sender, EventArgs e)
        {
            if (this._explorer._previewHandler is not { } previewHandler || this._source is not { } source)
                return;

            if (this._listView.SelectedIndices.Count is > 1 or 0) {
                previewHandler.ClearPreview();
                this.SetReferencesTargetToCurrentFolder();
                return;
            }

            var vo = source[this._listView.SelectedIndices[0]];
            if (vo.IsFolder) {
                previewHandler.ClearPreview();
                previewHandler.SetReferencesTarget(this._vfs?.GetFullPath(vo.Folder), true);
                return;
            }

            previewHandler.PreviewFile(vo.File);
            previewHandler.SetReferencesTarget(this._vfs?.GetFullPath(vo.File), false);
        }

        /// <summary>Shows the references to the current folder, when nothing in it is selected.</summary>
        public void SetReferencesTargetToCurrentFolder() =>
            this._explorer._previewHandler?.SetReferencesTarget(
                this.CurrentFolder is { } folder && this._vfs is { } tree ? tree.GetFullPath(folder) : null,
                true);

        private void WindowResized(object? sender, EventArgs e) => this.RecalculateNumberOfPreviewsToCache();

        public void ExecuteItems(List<IVirtualFile> files, List<IVirtualFolder> folders)
        {
            if (this._listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return;

            if (!files.Any() && folders.Count == 1) {
                this._explorer._navigationHandler?.NavigateTo(folders.First(), true);
                return;
            }

            if (this._vfs is null)
                return;

            foreach (var file in files.Take(16)) {
                this._explorer.OpenFileInViewer(
                    file,
                    source.ObjectList.Where(x => !x.IsFolder).Select(x => x.File),
                    this._explorer._navigationHandler?.CurrentFolder);
            }

            // TODO: do something
            Debug.Print("Do something");
        }

        // ReSharper disable once UnusedMember.Local
        public List<IVirtualFolder> GetSelectedFolders()
        {
            var folders = new List<IVirtualFolder>();
            if (this._listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return folders;

            for (var i = 0; i < this._listView.SelectedIndices.Count; i++) {
                var obj = source[this._listView.SelectedIndices[i]];
                if (obj.IsFolder)
                    folders.Add(obj.Folder);
            }

            return folders;
        }

        public List<IVirtualFile> GetSelectedFiles()
        {
            var folders = new List<IVirtualFile>();
            if (this._listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return folders;

            for (var i = 0; i < this._listView.SelectedIndices.Count; i++) {
                var obj = source[this._listView.SelectedIndices[i]];
                if (!obj.IsFolder)
                    folders.Add(obj.File);
            }

            return folders;
        }

        private void RecalculateNumberOfPreviewsToCache()
        {
            if (this._explorer.WindowState == FormWindowState.Minimized)
                return;

            if (this._source is not { } source)
                return;

            if (source.ImageThumbnailSize == 0)
                return;

            var size = this._explorer.Size;
            var horz = (size.Width + source.ImageThumbnailSize - 1) / source.ImageThumbnailSize;
            var vert = (size.Height + source.ImageThumbnailSize - 1) / source.ImageThumbnailSize;
            source.PreviewCacheCapacity = Math.Max(
                (int) Math.Ceiling(
                    horz * vert * Math.Max(2.0f, this._appConfig.PreviewThumbnailMinimumKeepInMemoryPages)),
                this._appConfig.PreviewThumbnailMinimumKeepInMemoryEntries);
        }

        private sealed class ThumbnailDecoration : IDecoration {
            private readonly FileListHandler _handler;

            public ThumbnailDecoration(FileListHandler handler)
            {
                this._handler = handler;
            }

            public OLVListItem? ListItem { get; set; }

            public OLVListSubItem? SubItem { get; set; }

            public void Draw(ObjectListView olv, Graphics g, Rectangle r)
            {
                if (this.ListItem is not { } listItem || this.ListItem.RowObject is not VirtualObject virtualObject ||
                    this._handler._source is not { } source)
                    return;

                Bitmap? bitmap = null;
                var smallIconSize = olv.SmallImageList!.ImageSize.Width;
                var iconSize = olv.View == View.LargeIcon ? olv.LogicalToDeviceUnits(32) : smallIconSize;
                var imageWidth = iconSize;
                var imageHeight = iconSize;
                var thumbnailSize = source.ImageThumbnailSize;
                var kind = ThumbnailKind.AssociationIcon;
                if (thumbnailSize != 0 && source.TryGetThumbnail(virtualObject, out bitmap, out kind)) {
                    try {
                        (imageWidth, imageHeight) = (bitmap.Width, bitmap.Height);
                        if (imageWidth > thumbnailSize)
                            (imageWidth, imageHeight) = (thumbnailSize, imageHeight * thumbnailSize / imageWidth);
                        if (imageHeight > thumbnailSize)
                            (imageWidth, imageHeight) = (imageWidth * thumbnailSize / imageHeight, thumbnailSize);
                    } catch (Exception) {
                        // pass
                    }
                }

                var iconBounds = listItem.GetBounds(ItemBoundsPortion.Icon);
                var x = iconBounds.Left + (iconBounds.Width - imageWidth) / 2;
                var y = iconBounds.Top + (iconBounds.Height - imageHeight) / 2;
                if (bitmap is not null) {
                    try {
                        g.DrawImage(bitmap, x, y, imageWidth, imageHeight);
                        // Info cards and icons have their own frames.
                        if (kind is ThumbnailKind.Image or ThumbnailKind.Render) {
                            using var pen = new Pen(Color.LightGray);
                            g.DrawRectangle(pen, x - 1, y - 1, imageWidth + 1, imageHeight + 1);
                        }

                        if (!virtualObject.IsFolder) {
                            FileIconBadge.DrawBadge(
                                g,
                                new(x, y, imageWidth, imageHeight),
                                virtualObject.Name,
                                olv.LogicalToDeviceUnits(32));
                        }

                        return;
                    } catch (Exception) {
                        // pass
                    }
                }

                // Files show their extension on the icon.
                if (!virtualObject.IsFolder &&
                    this._handler._fileIconBadge.Get(virtualObject.Name, imageWidth) is { } badgedIcon) {
                    g.DrawImage(badgedIcon, x, y, imageWidth, imageHeight);
                    return;
                }

                if (imageWidth <= smallIconSize && imageHeight <= smallIconSize)
                    olv.SmallImageList.Draw(g, x, y, virtualObject.IsFolder ? 1 : 0);
                else if ((virtualObject.IsFolder ? this._handler._folderIconLarge : this._handler._fileIconLarge) is
                         { } icon)
                    g.DrawIcon(icon, new Rectangle(x, y, imageWidth, imageHeight));
            }
        }
    }
}
