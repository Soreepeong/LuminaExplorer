using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightIdeasSoftware;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.App.Utils;
using LuminaExplorer.App.Window.FileViewers;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
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
            this._listView.SmallImageList.ImageSize = new(16, 16);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
                this._listView.SmallImageList.Images.Add(icon);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                this._listView.SmallImageList.Images.Add(icon);

            this._listView.LargeImageList = new();
            this._listView.LargeImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._listView.LargeImageList.ImageSize = new(32, 32);
            this._fileIconLarge = UiUtils.ExtractPeIcon("shell32.dll", 0, true);
            this._folderIconLarge = UiUtils.ExtractPeIcon("shell32.dll", 4, true);

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
                if (this._source is not null) this._source.CurrentFolder = value;

                this._explorer.colFilesFullPath.IsVisible = value is null;
                if (value is not null) this._explorer._searchHandler?.SearchAbort();
            }
        }

        public int ItemCount => this._source?.Count ?? 0;

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
            this._cboView.SelectedIndexChanged -= this.cboView_SelectedIndexChanged;

            this._explorer.Resize -= this.WindowResized;

            this._folderIconLarge?.Dispose();
            this._fileIconLarge?.Dispose();
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
            if (e.KeyCode == Keys.Enter) this.ExecuteItems(this.GetSelectedFiles(), this.GetSelectedFolders());
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
                return;
            }

            var vo = source[this._listView.SelectedIndices[0]];
            if (vo.IsFolder) {
                previewHandler.ClearPreview();
                return;
            }

            previewHandler.PreviewFile(vo.File);
        }

        private void WindowResized(object? sender, EventArgs e) => this.RecalculateNumberOfPreviewsToCache();

        public void ExecuteItems(List<IVirtualFile> files, List<IVirtualFolder> folders)
        {
            if (this._listView.VirtualListDataSource is not ExplorerListViewDataSource source)
                return;

            if (!files.Any() && folders.Count == 1) {
                this._explorer._navigationHandler?.NavigateTo(folders.First(), true);
                return;
            }

            if (this._vfs is not { } tree)
                return;

            foreach (var file in files.Take(16)) {
                Task<FileResource> fileResourceTask;
                if (this._explorer._previewHandler is { } previewHandler &&
                    previewHandler.TryGetAvailableFileResource(file, out var fileResource))
                    fileResourceTask = Task.FromResult(fileResource);
                else
                    fileResourceTask = tree.GetLookup(file).AsFileResource();
                fileResourceTask.ContinueWith(
                    fr => {
                        if (!fr.IsCompletedSuccessfully) {
                            MessageBox.Show(
                                $"Failed to open file \"{file.Name}\".\n\nError: {fr.Exception}",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Stop);
                            return;
                        }

                        if (MultiBitmapViewerControl.MaySupportFileResource(fr.Result)) {
                            var viewer = new TextureViewer();
                            viewer.SetFile(
                                tree,
                                file,
                                fr.Result,
                                this._explorer._navigationHandler?.CurrentFolder,
                                source.ObjectList.Where(x => !x.IsFolder).Select(x => x.File));
                            viewer.ShowRelativeTo(this._explorer);
                        }

                        switch (fr.Result) {
                            case ShcdFile f: {
                                var viewer = new TabbedTextViewer();
                                viewer.ShowShader(f, this._explorer);
                                break;
                            }
                            case ShpkFile f: {
                                var viewer = new TabbedTextViewer();
                                viewer.ShowShader(f, this._explorer);
                                break;
                            }
                            case MdlFile f: {
                                var viewer = new ModelViewer();
                                viewer.SetFile(tree, tree.RootFolder, file, f);
                                viewer.ShowRelativeTo(this._explorer);
                                ;
                                break;
                            }
                        }
                    },
                    TaskScheduler.FromCurrentSynchronizationContext());
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
                var imageWidth = olv.View == View.LargeIcon ? 32 : 16;
                var imageHeight = olv.View == View.LargeIcon ? 32 : 16;
                var thumbnailSize = source.ImageThumbnailSize;
                var isAssoc = true;
                if (thumbnailSize != 0 && source.TryGetThumbnail(virtualObject, out bitmap, out isAssoc)) {
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
                        if (!isAssoc) {
                            using var pen = new Pen(Color.LightGray);
                            g.DrawRectangle(pen, x - 1, y - 1, imageWidth + 1, imageHeight + 1);
                        }

                        return;
                    } catch (Exception) {
                        // pass
                    }
                }

                if (imageWidth <= 16 && imageHeight <= 16)
                    olv.SmallImageList!.Draw(g, x, y, virtualObject.IsFolder ? 1 : 0);
                else if ((virtualObject.IsFolder ? this._handler._folderIconLarge : this._handler._fileIconLarge) is
                         { } icon)
                    g.DrawIcon(icon, x, y);
            }
        }
    }
}
