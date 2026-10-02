using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private static readonly Guid ExplorerSaveFileGuid = Guid.Parse("b1f0f6a2-6a3e-4f57-9c1e-2d7e5a8c4b13");
    private static readonly Guid ExplorerSaveFolderGuid = Guid.Parse("c6d2e9b4-1f8a-4c0d-a7b3-5e9f2c6d8a21");

    private const int MaxErrorsShown = 15;

    private enum OverwriteChoice {
        Overwrite,
        Skip,
        Cancel,
    }

    /// <summary>Creates the context for exporting files of the current file system.</summary>
    private FileExportContext? CreateFileExportContext() =>
        this._vfs is { } vfs ? new(vfs, this.ExcelSchemaProvider) : null;

    /// <summary>Removes and disposes the items of a context menu, so that it can be filled again.</summary>
    private static void ClearContextMenu(ContextMenuStrip menu)
    {
        foreach (var item in menu.Items.Cast<ToolStripItem>().ToArray())
            item.Dispose();
        menu.Items.Clear();
    }

    /// <summary>Adds the file operations for the given items to a context menu.</summary>
    private void PopulateFileContextMenu(ToolStripItemCollection items, FileOperationTarget target)
    {
        if (target.Open is { } open) {
            var openItem = new ToolStripMenuItem("&Open", null, (_, _) => open());
            openItem.Font = new(openItem.Font, FontStyle.Bold);
            items.Add(openItem);
            items.Add(new ToolStripSeparator());
        }

        // Only the names are looked at here; whether and how each file is converted is decided while exporting.
        List<(FileExportMode Mode, string Text)> modes;
        if (target is { IsSingleFile: true, SingleFileName: { } name }) {
            var ext = Path.GetExtension(name);
            var kind = FileExport.GetConversionKind(name);
            modes = [(FileExportMode.AsIs, ext == string.Empty ? "as-is" : $"as {ext}")];
            if (FileExport.GetRecommendedExtension(kind) is { } recommended)
                modes.Add((FileExportMode.Recommended, $"as {recommended}"));
            else if (ext == string.Empty)
                modes.Add((FileExportMode.Recommended, "as recommended format"));
            if (kind == FileConversionKind.Texture)
                modes.Add((FileExportMode.Dds, "as .dds"));
            if (kind == FileConversionKind.Sound)
                modes.Add((FileExportMode.NativeAudio, "as native audio (.ogg/.wav)"));
        } else {
            var what = target.Count == 1
                ? target.FolderCount == 1 ? "folder" : "file"
                : $"{target.Count:N0} items";
            modes = [
                (FileExportMode.AsIs, $"{what} as-is"),
                (FileExportMode.Recommended, $"{what} as recommended formats"),
            ];
        }

        foreach (var (mode, text) in modes)
            items.Add(new ToolStripMenuItem($"Save {text}...", null, (_, _) => this.SaveItems(target, mode)));

        items.Add(new ToolStripSeparator());
        foreach (var (mode, text) in modes) {
            items.Add(
                new ToolStripMenuItem($"Copy {text}", null, (_, _) => this.CopyItems(target, mode)) {
                    ShortcutKeyDisplayString = mode == FileExportMode.AsIs ? "Ctrl+C" : null,
                });
        }

        items.Add(new ToolStripSeparator());
        items.Add(
            new ToolStripMenuItem(target.Count == 1 ? "Copy &path" : "Copy &paths", null, (_, _) => CopyPaths(target)) {
                ShortcutKeyDisplayString = "Ctrl+Shift+C",
            });
        items.Add(
            new ToolStripMenuItem(target.Count == 1 ? "Copy &name" : "Copy &names", null, (_, _) => CopyNames(target)));
    }

    /// <summary>Copies the full game paths of the items, one per line.</summary>
    private static void CopyPaths(FileOperationTarget target) => SetClipboardText(target.GetPaths());

    /// <summary>Copies the names of the items, one per line.</summary>
    private static void CopyNames(FileOperationTarget target) => SetClipboardText(target.GetNames());

    private static void SetClipboardText(IEnumerable<string> lines)
    {
        var text = string.Join("\r\n", lines);
        if (text == string.Empty)
            return;

        try {
            Clipboard.SetText(text);
        } catch (ExternalException e) {
            MessageBox.Show($"Failed to copy.\n\n{e.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Puts the items on the clipboard as files, converted according to <paramref name="mode"/>.</summary>
    private async void CopyItems(FileOperationTarget target, FileExportMode mode)
    {
        if (this.CreateFileExportContext() is not { } context)
            return;

        var progress = new FileExportProgress { Status = "Listing files..." };
        var dialog = new FileOperationProgressDialog("Copying", progress);
        dialog.Start(this);

        List<FileExportOutput> outputs;
        try {
            outputs = await Task.Run(() => PlanAllAsync(context, target, mode, progress, dialog.Token));
        } catch (OperationCanceledException) {
            return;
        } catch (Exception e) {
            MessageBox.Show(this, $"Failed to copy.\n\n{e.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        } finally {
            dialog.Complete();
        }

        if (outputs.Count > 0) {
            try {
                VirtualFileClipboard.SetFiles(outputs);
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    $"Failed to copy.\n\n{e.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        this.ShowFileOperationErrors(
            "Copy",
            progress,
            $"{outputs.Count:N0} file(s) were copied to the clipboard.");
    }

    /// <summary>Lists the files to put on the clipboard. The contents are generated later, when pasted.</summary>
    private static async Task<List<FileExportOutput>> PlanAllAsync(
        FileExportContext context,
        FileOperationTarget target,
        FileExportMode mode,
        FileExportProgress progress,
        CancellationToken cancellationToken)
    {
        var (files, folders) = await target.Resolve(cancellationToken);
        var sources = await FileExport.EnumerateAsync(
            context.Vfs,
            files,
            folders,
            n => progress.Status = $"Listing files... {n:N0} found",
            cancellationToken);
        progress.SetTotal(sources.Count);

        var results = new List<FileExportOutput>?[sources.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, sources.Count),
            new ParallelOptions {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            },
            async (i, ct) => {
                var source = sources[i];
                progress.Status = source.RelativePath;
                try {
                    results[i] = await FileExport.PlanAsync(context, source, mode, false, ct);
                } catch (Exception e) when (!ct.IsCancellationRequested) {
                    progress.Errors.Enqueue((source.RelativePath, e.Message));
                } finally {
                    progress.IncrementDone();
                }
            });

        var outputs = results.Where(x => x is not null).SelectMany(x => x!).ToList();
        FileExport.MakeUnique(outputs, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Contents are generated while the UI thread waits for them, so anything that may need the UI thread to
        // complete has to be ready beforehand.
        if (context.ExcelSchemaProvider is { } schemaProvider &&
            outputs.Any(x => FileExport.GetConversionKind(x.Source.File.Name) == FileConversionKind.ExcelSheet))
            await schemaProvider.GetSchemaSetAsync().WaitAsync(cancellationToken);

        return outputs;
    }

    /// <summary>Saves the items, converted according to <paramref name="mode"/>.</summary>
    private async void SaveItems(FileOperationTarget target, FileExportMode mode)
    {
        if (this.CreateFileExportContext() is not { } context)
            return;

        string? singlePath = null;
        string? directory = null;
        if (target is { IsSingleFile: true, SingleFileName: { } name }) {
            name = FileExport.SanitizeFileName(name);
            var ext = FileExport.GetExpectedExtension(name, mode);
            using var sfd = new SaveFileDialog();
            sfd.ClientGuid = ExplorerSaveFileGuid;
            sfd.Title = $"Save {name}";
            sfd.AddExtension = true;
            sfd.OverwritePrompt = true;
            sfd.FileName = Path.GetFileNameWithoutExtension(name) + ext;
            sfd.Filter = mode == FileExportMode.NativeAudio && ext == ".ogg"
                ? "Audio file (*.ogg;*.wav)|*.ogg;*.wav|All files (*.*)|*.*"
                : ext == string.Empty
                    ? "All files (*.*)|*.*"
                    : $"{ext.TrimStart('.').ToUpperInvariant()} file (*{ext})|*{ext}|All files (*.*)|*.*";
            if (sfd.ShowDialog(this) != DialogResult.OK)
                return;
            singlePath = sfd.FileName;
        } else {
            using var fbd = new FolderBrowserDialog();
            fbd.ClientGuid = ExplorerSaveFolderGuid;
            fbd.Description = mode == FileExportMode.AsIs
                ? $"Save {target.Count:N0} item(s) as-is to"
                : $"Save {target.Count:N0} item(s) as recommended formats to";
            fbd.UseDescriptionForTitle = true;
            fbd.ShowNewFolderButton = true;
            if (fbd.ShowDialog(this) != DialogResult.OK)
                return;
            directory = fbd.SelectedPath;
        }

        var progress = new FileExportProgress { Status = "Listing files..." };
        var dialog = new FileOperationProgressDialog("Saving", progress);
        dialog.Start(this);
        try {
            await Task.Run(
                () => SaveAllAsync(
                    context,
                    target,
                    mode,
                    singlePath,
                    directory,
                    progress,
                    this.AskOverwrite,
                    dialog.Token));
        } catch (OperationCanceledException) {
            // Already saved files are kept.
        } catch (Exception e) {
            progress.Errors.Enqueue((string.Empty, e.Message));
        } finally {
            dialog.Complete();
        }

        this.ShowFileOperationErrors("Save", progress, $"{progress.FilesWritten:N0} file(s) were saved.");
    }

    /// <summary>Saves the items into <paramref name="singlePath"/> (for a single file) or into
    /// <paramref name="directory"/>.</summary>
    private static async Task SaveAllAsync(
        FileExportContext context,
        FileOperationTarget target,
        FileExportMode mode,
        string? singlePath,
        string? directory,
        FileExportProgress progress,
        Func<string, OverwriteChoice> askOverwrite,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var (files, folders) = await target.Resolve(cts.Token);
        var sources = await FileExport.EnumerateAsync(
            context.Vfs,
            files,
            folders,
            n => progress.Status = $"Listing files... {n:N0} found",
            cts.Token);
        progress.SetTotal(sources.Count);

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        OverwriteChoice? overwriteChoice = null;
        using var overwriteLock = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(
            sources,
            new ParallelOptions {
                CancellationToken = cts.Token,
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            },
            async (source, ct) => {
                progress.Status = source.RelativePath;
                try {
                    var outputs = await FileExport.PlanAsync(context, source, mode, true, ct);
                    lock (usedPaths)
                        FileExport.MakeUnique(outputs, usedPaths);

                    foreach (var output in outputs) {
                        var path = GetSavePath(output, outputs.Count, mode, singlePath, directory!);
                        if (singlePath is null && File.Exists(path)) {
                            await overwriteLock.WaitAsync(ct);
                            try {
                                overwriteChoice ??= askOverwrite(path);
                            } finally {
                                overwriteLock.Release();
                            }

                            if (overwriteChoice == OverwriteChoice.Cancel)
                                await cts.CancelAsync();
                            ct.ThrowIfCancellationRequested();
                            if (overwriteChoice == OverwriteChoice.Skip)
                                continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        try {
                            await using var stream = new FileStream(
                                path,
                                FileMode.Create,
                                FileAccess.ReadWrite,
                                FileShare.None);
                            await output.WriteAsync(stream, ct);
                        } catch (Exception) {
                            try {
                                File.Delete(path);
                            } catch (Exception) {
                                // pass
                            }

                            throw;
                        }

                        progress.IncrementFilesWritten();
                    }
                } catch (Exception e) when (!ct.IsCancellationRequested) {
                    progress.Errors.Enqueue((source.RelativePath, e.Message));
                } finally {
                    progress.IncrementDone();
                }
            });
    }

    private static string GetSavePath(
        FileExportOutput output,
        int outputCount,
        FileExportMode mode,
        string? singlePath,
        string directory)
    {
        if (singlePath is null)
            return Path.Combine(directory, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        // The chosen name is used as-is, unless the file turns out to be written as a different type or as
        // multiple files.
        if (outputCount == 1 &&
            (mode == FileExportMode.AsIs ||
                string.Equals(Path.GetExtension(singlePath), output.Extension, StringComparison.OrdinalIgnoreCase)))
            return singlePath;

        return Path.Combine(
            Path.GetDirectoryName(singlePath)!,
            Path.GetFileNameWithoutExtension(singlePath) + output.Suffix + output.Extension);
    }

    private OverwriteChoice AskOverwrite(string path) =>
        (OverwriteChoice) this.Invoke(
            () => MessageBox.Show(
                    this,
                    $"Some files already exist, such as:\n{path}\n\n" +
                    "Yes: overwrite existing files\nNo: skip existing files\nCancel: stop saving",
                    "Save",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question) switch {
                    DialogResult.Yes => OverwriteChoice.Overwrite,
                    DialogResult.No => OverwriteChoice.Skip,
                    _ => OverwriteChoice.Cancel,
                });

    /// <summary>Shows the failures of a file operation, if any.</summary>
    private void ShowFileOperationErrors(string operation, FileExportProgress progress, string summary)
    {
        if (progress.Errors.IsEmpty || this.IsDisposed)
            return;

        var errors = progress.Errors.ToArray();
        var sb = new StringBuilder();
        sb.AppendLine(summary);
        sb.AppendLine();
        sb.AppendLine($"{errors.Length:N0} item(s) failed:");
        foreach (var (path, message) in errors.Take(MaxErrorsShown))
            sb.AppendLine(path == string.Empty ? message : $"{path}: {message}");
        if (errors.Length > MaxErrorsShown)
            sb.AppendLine($"... and {errors.Length - MaxErrorsShown:N0} more.");

        MessageBox.Show(this, sb.ToString(), operation, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Items that file operations work on.</summary>
    private sealed class FileOperationTarget {
        /// <summary>Gets the number of files.</summary>
        public required int FileCount { get; init; }

        /// <summary>Gets the number of folders.</summary>
        public required int FolderCount { get; init; }

        /// <summary>Gets the name of the file, if this is a single file.</summary>
        public string? SingleFileName { get; init; }

        /// <summary>Gets the full game paths of the items.</summary>
        public required Func<IEnumerable<string>> GetPaths { get; init; }

        /// <summary>Gets the names of the items.</summary>
        public required Func<IEnumerable<string>> GetNames { get; init; }

        /// <summary>Gets the items.</summary>
        public required Func<CancellationToken, Task<(IReadOnlyList<IVirtualFile> Files,
            IReadOnlyList<IVirtualFolder> Folders)>> Resolve { get; init; }

        /// <summary>Gets the action that opens the items, if any.</summary>
        public Action? Open { get; init; }

        public int Count => this.FileCount + this.FolderCount;

        public bool IsSingleFile => this is { FileCount: 1, FolderCount: 0 };

        public static FileOperationTarget FromItems(
            IVirtualFileSystem vfs,
            IReadOnlyList<IVirtualFile> files,
            IReadOnlyList<IVirtualFolder> folders,
            Action? open) => new() {
            FileCount = files.Count,
            FolderCount = folders.Count,
            SingleFileName = files.Count == 1 && folders.Count == 0 ? files[0].Name : null,
            GetPaths = () => folders.Select(vfs.GetFullPath).Concat(files.Select(vfs.GetFullPath))
                .Select(x => x.TrimStart('/')),
            GetNames = () => folders.Select(x => x.Name.Trim('/')).Concat(files.Select(x => x.Name)),
            Resolve = _ => Task.FromResult((files, folders)),
            Open = open,
        };
    }
}
