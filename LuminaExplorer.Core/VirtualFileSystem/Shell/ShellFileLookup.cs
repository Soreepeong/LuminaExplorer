using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using Lumina;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public sealed class ShellFileLookup : IVirtualFileLookup {
    public ShellFileLookup(ShellFileSystem fs, ShellFile file) {
        FileSystem = fs;
        FileTyped = file;

        Type = Path.GetExtension(file.Name).ToLowerInvariant() switch {
            _ when Size == 0 => FileType.Empty,
            ".tex" => FileType.Texture,
            ".mdl" => FileType.Model,
            _ => FileType.Standard,
        };

        var b = fs.BindToObject<IShellItem2>(file.Idl);
        try {
            b.GetUInt64(ref PropertyKey.PKEY_Size, out var size);
            Size = (long) size;
            b.GetUInt64(ref PropertyKey.PKEY_FileAllocationSize, out size);
            ReservedBytes = OccupiedBytes = (long) size;
        } finally {
            Marshal.ReleaseComObject(b);
        }
    }

    public void Dispose() {}

    public ShellFileSystem FileSystem { get; }
    public ShellFile FileTyped { get; }
    
    public IVirtualFile File => FileTyped; 
    public FileType Type { get; }
    public long Size { get; }
    public long ReservedBytes { get; }
    public long OccupiedBytes { get; }

    public Stream CreateStream() {
        var b = FileSystem.BindToStorage<IStream>(FileTyped.Idl);
        try {
            Debugger.Break();
            throw new NotImplementedException();
        } finally {
            Marshal.ReleaseComObject(b);
        }
    }

    public unsafe Task<byte[]> ReadAll(CancellationToken cancellationToken = default) => Task.Run(() => {
        var istream = FileSystem.BindToStorage<IStream>(FileTyped.Idl);
        try {
            istream.Stat(out var s, 1);
            var b = new byte[s.cbSize];
            var rd = 0l;
            istream.Read(b, 0, (nint) (&rd));
            if (rd != b.Length)
                throw new IOException();
            return b;
        } finally {
            Marshal.ReleaseComObject(istream);
        }
    }, cancellationToken);

    private FileResource AsFileResourceImpl(LuminaBinaryReader reader, byte[] buffer, Type type) {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        if (!type.IsAssignableTo(typeof(FileResource)))
            throw new ArgumentException(null, nameof(type));

        var file = (FileResource) Activator.CreateInstance(type)!;
        var luminaFileInfo = new LuminaFileInfo {
            Type = Type,
        };

        var pfp = new ParsedFilePath();
        typeof(ParsedFilePath).GetProperty("Path", bindingFlags)!.SetValue(pfp, FileSystem.GetFullPath(FileTyped));

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
}
