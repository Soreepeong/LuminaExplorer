using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Utils;

/// <summary>Puts exported files on the clipboard as virtual files (CFSTR_FILEDESCRIPTORW and CFSTR_FILECONTENTS),
/// so that pasting into File Explorer (or any other target that accepts virtual files) creates them.</summary>
/// <remarks>
/// <para>The data object is put on the clipboard directly with <c>OleSetClipboard</c>, without flushing: the
/// clipboard keeps a reference to it, and the contents of each file are generated only when a paste target asks
/// for them. <c>Clipboard.SetDataObject</c> is not used, as it always wraps the object in a WinForms
/// <see cref="DataObject"/>, and with <c>copy: true</c> would call <c>OleFlushClipboard</c>, which renders every
/// format into memory at once and cannot keep more than one <c>FileContents</c> stream.</para>
/// <para>Paste target requests arrive on the UI thread (the apartment that set the clipboard). The contents are
/// generated in the background, a few files ahead of the one being asked for; the UI thread waits for each file.
/// </para>
/// <para>The files can be pasted only while the application is running. On exit, <see cref="ReleaseIfOwned"/>
/// removes the data object from the clipboard if it is still there; otherwise the clipboard would keep listing the
/// formats without being able to provide them.</para>
/// </remarks>
public static class VirtualFileClipboard {
    private const int Lookahead = 3;

    private static VirtualFileDataObject? _current;

    /// <summary>Puts the files on the clipboard.</summary>
    /// <param name="outputs">Files to provide. Their relative paths should be unique.</param>
    /// <returns>The data object put on the clipboard.</returns>
    public static VirtualFileDataObject SetFiles(IReadOnlyList<FileExportOutput> outputs)
    {
        var dataObject = CreateDataObject(outputs);
        Marshal.ThrowExceptionForHR(OleSetClipboard(dataObject));
        _current = dataObject;
        return dataObject;
    }

    /// <summary>Creates a data object providing the files.</summary>
    /// <param name="outputs">Files to provide. Their relative paths should be unique.</param>
    public static VirtualFileDataObject CreateDataObject(IReadOnlyList<FileExportOutput> outputs)
    {
        var provider = new ContentProvider(outputs);
        var descriptors = new List<VirtualFileDataObject.FileDescriptor>();

        // Directories come first, parents before children, so that the targets can create them in order.
        var directories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs) {
            var dir = output.Source.RelativeDirectory.TrimEnd('/');
            while (dir != string.Empty && directories.Add(dir))
                dir = dir.Contains('/') ? dir[..dir.LastIndexOf('/')] : string.Empty;
        }

        descriptors.AddRange(
            directories.Select(
                x => new VirtualFileDataObject.FileDescriptor {
                    Name = x.Replace('/', '\\'),
                    IsDirectory = true,
                }));

        for (var i = 0; i < outputs.Count; i++) {
            var index = i;
            descriptors.Add(
                new() {
                    Name = outputs[i].RelativePath.Replace('/', '\\'),
                    Length = outputs[i].Length,
                    StreamContents = stream => provider.WriteTo(index, stream),
                });
        }

        var dataObject = new VirtualFileDataObject();
        dataObject.SetData(descriptors);
        dataObject.PreferredDropEffect = DragDropEffects.Copy;
        return dataObject;
    }

    /// <summary>Tests if the given data object is on the clipboard.</summary>
    public static bool IsCurrent(VirtualFileDataObject dataObject) => OleIsCurrentClipboard(dataObject) == 0;

    /// <summary>Removes the data object put by <see cref="SetFiles"/> from the clipboard, if it is still there.
    /// </summary>
    public static void ReleaseIfOwned()
    {
        if (Interlocked.Exchange(ref _current, null) is not { } current)
            return;

        try {
            if (IsCurrent(current))
                OleSetClipboard(null);
        } catch (Exception e) {
            Debug.Print($"Failed to release the clipboard: {e}");
        }
    }

    [DllImport("ole32.dll")]
    private static extern int OleSetClipboard(System.Runtime.InteropServices.ComTypes.IDataObject? pDataObj);

    [DllImport("ole32.dll")]
    private static extern int OleIsCurrentClipboard(System.Runtime.InteropServices.ComTypes.IDataObject pDataObj);

    /// <summary>Generates the contents of the files on request, a few files ahead.</summary>
    private sealed class ContentProvider {
        private readonly IReadOnlyList<FileExportOutput> _outputs;
        private readonly Dictionary<int, Task<byte[]>> _tasks = new();

        public ContentProvider(IReadOnlyList<FileExportOutput> outputs) => this._outputs = outputs;

        public void WriteTo(int index, Stream stream)
        {
            Task<byte[]> task;
            lock (this._tasks) {
                task = this.GetOrStart(index);
                for (var i = index + 1; i <= index + Lookahead && i < this._outputs.Count; i++)
                    this.GetOrStart(i);
            }

            byte[] data;
            try {
                // The contents are generated in the thread pool without depending on this thread, so this cannot
                // deadlock.
                data = task.GetAwaiter().GetResult();
            } catch (Exception e) {
                Debug.Print($"Failed to provide {this._outputs[index].RelativePath}: {e}");
                throw;
            } finally {
                lock (this._tasks)
                    this._tasks.Remove(index);
            }

            stream.Write(data, 0, data.Length);
        }

        private Task<byte[]> GetOrStart(int index)
        {
            if (this._tasks.TryGetValue(index, out var task))
                return task;

            var output = this._outputs[index];
            return this._tasks[index] = Task.Run(
                async () => {
                    using var ms = new MemoryStream();
                    await output.WriteAsync(ms, CancellationToken.None);
                    return ms.ToArray();
                });
        }
    }
}
