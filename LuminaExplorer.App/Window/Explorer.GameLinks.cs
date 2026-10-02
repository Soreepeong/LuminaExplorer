using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Files.Excel;
using LuminaExplorer.App.Window.FileViewers;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    /// <summary>Opens a file in the viewer for its type, as if it were double-clicked in the file list.</summary>
    /// <param name="file">File to open.</param>
    /// <param name="siblings">Files to browse through in viewers that support it; defaults to the files in the folder
    /// of <paramref name="file"/>.</param>
    /// <param name="folder">Folder of <paramref name="siblings"/>; defaults to the folder of
    /// <paramref name="file"/>.</param>
    public void OpenFileInViewer(
        IVirtualFile file,
        IEnumerable<IVirtualFile>? siblings = null,
        IVirtualFolder? folder = null)
    {
        if (this._vfs is not { } tree)
            return;

        Task<FileResource> fileResourceTask;
        if (this._previewHandler is { } previewHandler &&
            previewHandler.TryGetAvailableFileResource(file, out var fileResource))
            fileResourceTask = Task.FromResult(fileResource);
        else
            fileResourceTask = tree.GetLookup(file).AsFileResource();
        fileResourceTask.ContinueWith(
            fr => {
                if (this.IsDisposed)
                    return;

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
                        folder ?? file.Parent,
                        siblings ?? tree.GetFiles(file.Parent));
                    viewer.ShowRelativeTo(this);
                }

                switch (fr.Result) {
                    case ShcdFile f: {
                        var viewer = new TabbedTextViewer();
                        viewer.ShowShader(f, this);
                        break;
                    }
                    case ShpkFile f: {
                        var viewer = new TabbedTextViewer();
                        viewer.ShowShader(f, this);
                        break;
                    }
                    case MdlFile f: {
                        var viewer = new ModelViewer();
                        viewer.SetFile(tree, tree.RootFolder, file, f);
                        viewer.ShowRelativeTo(this);
                        break;
                    }
                    case ExcelHeaderFile or ExcelDataFile: {
                        var viewer = this.CreateExcelViewer();
                        viewer.SetFile(tree, tree.RootFolder, file, fr.Result, this.ExcelSchemaProvider);
                        viewer.ShowRelativeTo(this);
                        break;
                    }
                    case ScdFile f: {
                        var viewer = new ScdPlayer();
                        viewer.SetFile(tree, file, f);
                        viewer.ShowRelativeTo(this);
                        break;
                    }
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Opens the file at a game path in its viewer, or shows a folder in this window.</summary>
    /// <param name="path">Path, without a leading slash; folders end with a slash.</param>
    public async void OpenGamePath(string path)
    {
        if (this._vfs is not { } tree)
            return;

        path = path.Replace('\\', '/').TrimStart('/');
        if (path.EndsWith('/')) {
            this.ShowPathInExplorer(path);
            return;
        }

        IVirtualFile? file;
        try {
            if (tree is SqpackFileSystem sqfs)
                await sqfs.SuggestFullPathAsync(path);
            else
                tree.SuggestFullPath(path);
            file = await tree.LocateFile(tree.RootFolder, path);
        } catch (Exception) {
            file = null;
        }

        if (this.IsDisposed)
            return;

        if (file is null) {
            MessageBox.Show(
                $"\"{path}\" could not be found.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            return;
        }

        this.OpenFileInViewer(file);
    }

    /// <summary>Navigates to the folder of a game path, and selects the file if it is a file.</summary>
    /// <param name="path">Path, without a leading slash; folders end with a slash.</param>
    public async void ShowPathInExplorer(string path)
    {
        if (this._vfs is not { } tree || this._navigationHandler is not { } nav)
            return;

        path = path.Replace('\\', '/').TrimStart('/');
        var slash = path.LastIndexOf('/');
        var folderPath = path[..(slash + 1)];
        var fileName = path[(slash + 1)..];

        // Brought to the front if requested from another window of this app, such as a sheet viewer.
        if (this.WindowState == FormWindowState.Minimized)
            this.WindowState = FormWindowState.Normal;
        if (ActiveForm is not null && ActiveForm != this)
            this.Activate();

        try {
            // Folders and files whose names are not known yet are shown with their hashes; name them first.
            if (tree is SqpackFileSystem sqfs)
                await sqfs.SuggestFullPathAsync(fileName.Length != 0 ? path : path + "_");
            else if (fileName.Length != 0)
                tree.SuggestFullPath(path);
            if (this.IsDisposed)
                return;

            if (this._fileTreeHandler is { IsFiltering: false } fileTreeHandler)
                await fileTreeHandler.ExpandTreeTo(folderPath);
            else
                await nav.NavigateTo(folderPath);
        } catch (Exception) {
            return;
        }

        // Navigation to the folder may still be queued; select the file once it has been loaded.
        if (fileName.Length != 0 && !this.IsDisposed)
            this.BeginInvoke(() => this._fileListHandler?.SelectFileAfterLoad(fileName));
    }

    /// <summary>Creates a sheet viewer, whose links to game paths lead to this window.</summary>
    public ExcelViewer CreateExcelViewer() =>
        new() {
            ShowPathInExplorer = this.ShowPathInExplorer,
            OpenPath = this.OpenGamePath,
        };

    /// <summary>Opens a sheet, scrolled to a cell.</summary>
    public void OpenSheetCell(string sheetName, uint rowId, ushort subrowId, int columnIndex)
    {
        if (this._vfs is not { } tree)
            return;

        var path = $"exd/{sheetName}{ExcelSheetFileNames.HeaderExtension}";
        Task.Run(
                async () => {
                    var file = await tree.LocateFile(tree.RootFolder, path)
                        ?? throw new FileNotFoundException($"\"{path}\" could not be found.");
                    using var lookup = tree.GetLookup(file);
                    return (File: file, Resource: await lookup.AsFileResource());
                })
            .ContinueWith(
                r => {
                    if (this.IsDisposed)
                        return;

                    try {
                        var (file, resource) = r.Result;
                        var viewer = this.CreateExcelViewer();
                        viewer.SetFile(tree, tree.RootFolder, file, resource, this.ExcelSchemaProvider);
                        viewer.GoToCell(rowId, subrowId, columnIndex);
                        viewer.ShowRelativeTo(this);
                    } catch (Exception e) {
                        var message = (e as AggregateException)?.InnerException?.Message ?? e.Message;
                        MessageBox.Show(
                            $"Failed to open \"{path}\".\n\nError: {message}",
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Stop);
                    }
                },
                default,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext());
    }
}
