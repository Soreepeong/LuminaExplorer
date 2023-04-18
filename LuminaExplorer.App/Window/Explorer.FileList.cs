using BrightIdeasSoftware;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private CancellationTokenSource? _previewCancellationTokenSource;

    private void Constructor_FileList() {
        lvwFiles.SmallImageList = new();
        lvwFiles.SmallImageList.ColorDepth = ColorDepth.Depth32Bit;
        lvwFiles.SmallImageList.ImageSize = new(16, 16);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
            lvwFiles.SmallImageList.Images.Add(icon);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
            lvwFiles.SmallImageList.Images.Add(icon);
        
        lvwFiles.LargeImageList = new();
        lvwFiles.LargeImageList.ColorDepth = ColorDepth.Depth32Bit;
        lvwFiles.LargeImageList.ImageSize = new(32, 32);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, true)!)
            lvwFiles.LargeImageList.Images.Add(icon);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, true)!)
            lvwFiles.LargeImageList.Images.Add(icon);
        
        lvwFiles.VirtualListDataSource = new ExplorerListViewDataSource(lvwFiles, _vsp);
        lvwFiles.PrimarySortColumn = colFilesName;
        lvwFiles.PrimarySortOrder = SortOrder.Ascending;
        lvwFiles.MouseWheel += lvwFiles_MouseWheel;
        
        _vsp.FolderChanged += SqPackTree_FolderChanged_FileList;
        _vsp.FileChanged += SqPackTree_FileChanged_FileList;
        
        cboView.SelectedIndex = cboView.Items.Count - 1; // detail view
    }

    private void Dispose_FileList() {
        if (lvwFiles.VirtualListDataSource is ExplorerListViewDataSource source) {
            source.Dispose();
            
            // cannot set to null
            lvwFiles.VirtualListDataSource = new AbstractVirtualListDataSource(lvwFiles);
        }
    }

    private void SqPackTree_FileChanged_FileList(VirtualFile changedFile) {
        while (_currentFolder == changedFile.Parent) {
            if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                break;

            if (source.FirstOrDefault(x => x.File == changedFile) is { } modelObject) {
                modelObject.Name = changedFile.Name;
                modelObject.FullPath = _vsp.GetFullPath(changedFile);
            }

            break;
        }
    }

    private void SqPackTree_FolderChanged_FileList(VirtualFolder changedFolder, VirtualFolder[]? previousPathFromRoot) {
        while (_currentFolder == changedFolder) {
            if (lvwFiles.VirtualListDataSource is not ExplorerListViewDataSource source)
                break;

            if (source.FirstOrDefault(x => x.Folder == changedFolder) is { } modelObject) {
                modelObject.Name = changedFolder.Name;
                modelObject.FullPath = _vsp.GetFullPath(changedFolder);
            }

            break;
        }
    }
    
    private void cboView_SelectedIndexChanged(object sender, EventArgs e) {
        lvwFiles.View = cboView.SelectedIndex switch {
            <= 6 => View.LargeIcon,
            7 => View.SmallIcon,
            8 => View.List,
            9 => View.Details,
            _ => throw new FailFastException("cboView.SelectedIndex >= cboView.SelectedIndex.Items.Count?"),
        };

        if (lvwFiles.VirtualListDataSource is ExplorerListViewDataSource source) {
            source.ImageThumbnailSize = cboView.SelectedIndex switch {
                <= 6 => 256 - 32 * cboView.SelectedIndex,
                _ => 0,
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

        var files = GetListViewSelectedFiles();
        if (files.Any()) {
            var virtualFileDataObject = new VirtualFileDataObject();

            // Provide a virtual file (generated on demand) containing the letters 'a'-'z'
            virtualFileDataObject.SetData(files.Select(x => new VirtualFileDataObject.FileDescriptor {
                Name = x.Name,
                Length = _vsp.GetLookup(x).Size,
                StreamContents = dstStream => {
                    using var srcStream = _vsp.GetLookup(x).CreateStream();
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
            case Keys.Left when e is {Control: false, Alt: true, Shift: false}:
            case Keys.Back when e is {Control: false, Alt: false, Shift: false}:
            case Keys.BrowserBack:
                NavigateBack();
                break;
            case Keys.Right when e is {Control: false, Alt: true, Shift: false}:
            case Keys.BrowserForward:
                NavigateForward();
                break;
            case Keys.Up when e is {Control: false, Alt: true, Shift: false}:
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
        _vsp.GetLookup(vo.File)
            .AsFileResource(_previewCancellationTokenSource.Token)
            .ContinueWith(fr => {
                if (!fr.IsCompletedSuccessfully)
                    return;
                ppgPreview.SelectedObject = new WrapperTypeConverter().ConvertFrom(fr.Result);
                hbxPreview.ByteProvider = new FileResourceByteProvider(fr.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ReSharper disable once UnusedMember.Local
    private List<VirtualFolder> GetListViewSelectedFolders() {
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

    private List<VirtualFile> GetListViewSelectedFiles() {
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
}