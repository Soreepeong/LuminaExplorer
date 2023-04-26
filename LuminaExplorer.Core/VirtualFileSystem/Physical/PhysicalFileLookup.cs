using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Physical;

public sealed partial class PhysicalFileLookup : IVirtualFileLookup {
    public PhysicalFileLookup(PhysicalFile physicalFile) {
        FileTyped = physicalFile;

        try {
            Size = FileTyped.FileInfo.Length;
        } catch (Exception) {
            Size = 0;
        }

        Type = FileTyped.FileInfo.Extension.ToLowerInvariant() switch {
            _ when Size == 0 => FileType.Empty,
            ".tex" => FileType.Texture,
            ".mdl" => FileType.Model,
            _ => FileType.Standard,
        };

        if (GetDiskFreeSpaceW(
                FileTyped.FileInfo.Directory!.Root.FullName,
                out var sectorsPerCluster,
                out var bytesPerSector, out _,
                out _) != 0) {
            var clusterSize = sectorsPerCluster * bytesPerSector;
            var low = GetCompressedFileSizeW(FileTyped.FileInfo.FullName, out var high);
            if (low != 0xFFFFFFFFu || Marshal.GetLastWin32Error() == 0) {
                var size = (long) high << 32 | low;
                ReservedBytes = OccupiedBytes = ((size + clusterSize - 1) / clusterSize) * clusterSize;
            }
        }
    }

    public void Dispose() { }

    public PhysicalFile FileTyped { get; }

    public IVirtualFile File => FileTyped;

    public FileType Type { get; }

    public long Size { get; }

    public long ReservedBytes { get; }

    public long OccupiedBytes { get; }

    public Stream CreateStream() => FileTyped.FileInfo.OpenRead();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default) =>
        System.IO.File.ReadAllBytesAsync(FileTyped.FileInfo.FullName, cancellationToken);

    private FileResource AsFileResourceImpl(LuminaBinaryReader reader, byte[] buffer, Type type) {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (!type.IsAssignableTo(typeof(FileResource)))
            throw new ArgumentException(null, nameof(type));

        var file = (FileResource) Activator.CreateInstance(type)!;
        var luminaFileInfo = new LuminaFileInfo {
            Type = Type,
        };

        var pfp = new ParsedFilePath();
        typeof(ParsedFilePath).GetProperty("Path", bindingFlags)!.SetValue(pfp, FileTyped.FileInfo.FullName);

        typeof(FileResource).GetProperty("FileInfo", bindingFlags)!.SetValue(file, luminaFileInfo);
        typeof(FileResource).GetProperty("FilePath", bindingFlags)!.SetValue(file, pfp);
        typeof(FileResource).GetProperty("Data", bindingFlags)!.SetValue(file, buffer);
        typeof(FileResource).GetProperty("Reader", bindingFlags)!.SetValue(file, reader);
        typeof(FileResource).GetMethod("LoadFile", bindingFlags)!.Invoke(file, null);
        return file;
    }

    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource =>
        Task.Factory.StartNew(
            () => ReadAll(cancellationToken)
                .ContinueWith(buffer => {
                    var reader = new LuminaBinaryReader(buffer.Result);
                    try {
                        cancellationToken.ThrowIfCancellationRequested();
                        return (T) AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(T));
                    } catch (Exception) {
                        reader.Dispose();
                        throw;
                    }
                }, cancellationToken),
            cancellationToken,
            TaskCreationOptions.None,
            TaskScheduler.Default
        ).Unwrap();

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default) =>
        Task.Factory.StartNew(
            () => ReadAll(cancellationToken)
                .ContinueWith(buffer => {
                    var reader = new LuminaBinaryReader(buffer.Result);
                    var possibleTypes = IVirtualFileLookup.FindPossibleTypes(this, reader);

                    foreach (var f in possibleTypes) {
                        cancellationToken.ThrowIfCancellationRequested();
                        try {
                            return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, f);
                        } catch (Exception) {
                            // pass 
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return AsFileResourceImpl(reader.WithSeek(0), buffer.Result, typeof(FileResource));
                }, cancellationToken),
            cancellationToken,
            TaskCreationOptions.None,
            TaskScheduler.Default
        ).Unwrap();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetCompressedFileSizeW(
        [MarshalAs(UnmanagedType.LPWStr)] in string lpFileName,
        [MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetDiskFreeSpaceW(
        [MarshalAs(UnmanagedType.LPWStr)] in string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);
}
