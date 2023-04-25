using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Structs;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.Core.VirtualFileSystem;

public interface IVirtualFileLookup  : IDisposable {
    public VirtualFile File { get; }

    public FileType Type { get; }

    public uint Size { get; }

    public ulong ReservedBytes { get; }

    public ulong OccupiedBytes { get; }

    public Stream CreateStream();

    public Task<byte[]> ReadAll(CancellationToken cancellationToken = default);

    public Task<FileResource> AsFileResource(CancellationToken cancellationToken = default);

    public Task<T> AsFileResource<T>(CancellationToken cancellationToken = default) where T : FileResource;
}