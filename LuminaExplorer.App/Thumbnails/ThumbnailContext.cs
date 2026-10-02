using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumina.Data;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>State shared between thumbnail requests: file system access and small caches of related files.</summary>
public sealed class ThumbnailContext : IDisposable {
    private readonly CancellationTokenSource _disposing = new();
    private readonly AsyncLruCache<string, SklbFile?> _sklbCache = new(8);

    public ThumbnailContext(IVirtualFileSystem? vfs)
    {
        this.Vfs = vfs;
    }

    public void Dispose()
    {
        this._disposing.Cancel();
        this._sklbCache.Dispose();
    }

    public IVirtualFileSystem? Vfs { get; }

    /// <summary>Locates a file by its full path from the root of the file system.</summary>
    public async Task<IVirtualFile?> LocateFile(string fullPath, CancellationToken cancellationToken)
    {
        if (this.Vfs is not { } vfs)
            return null;
        try {
            return await vfs.LocateFile(vfs.RootFolder, fullPath).WaitAsync(cancellationToken);
        } catch (Exception e) when (e is not OperationCanceledException) {
            return null;
        }
    }

    /// <summary>Loads a file resource by its full path, or returns null if not found or failed to load.</summary>
    public async Task<T?> LoadFileResource<T>(string fullPath, CancellationToken cancellationToken)
        where T : FileResource
    {
        if (this.Vfs is not { } vfs || await this.LocateFile(fullPath, cancellationToken) is not { } file)
            return null;
        try {
            using var lookup = vfs.GetLookup(file);
            return await lookup.AsFileResource<T>(cancellationToken);
        } catch (Exception e) when (e is not OperationCanceledException) {
            return null;
        }
    }

    /// <summary>Loads a texture by its full path, at least as large as given if possible.</summary>
    public async Task<Bitmap?> LoadTexture(string fullPath, int minEdgeLength, CancellationToken cancellationToken)
    {
        if (this.Vfs is not { } vfs || await this.LocateFile(fullPath, cancellationToken) is not { } file)
            return null;
        try {
            using var lookup = vfs.GetLookup(file);
            await using var stream = lookup.CreateStream();
            var platformId = vfs is Core.VirtualFileSystem.Sqpack.SqpackFileSystem sqfs
                ? sqfs.PlatformId
                : Lumina.Data.Structs.PlatformId.Win32;
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            return ext is ".tex" or ".atex"
                ? await stream.ExtractMipmapOfSizeAtLeastForTex(minEdgeLength, platformId, cancellationToken)
                : await stream.ExtractMipmapOfSizeAtLeast(minEdgeLength, platformId, cancellationToken);
        } catch (Exception e) when (e is not OperationCanceledException) {
            return null;
        }
    }

    /// <summary>Gets a skeleton by its full path, from a small cache shared between requests.</summary>
    /// <remarks>The returned object is shared; do not modify.</remarks>
    public Task<SklbFile?> GetSklb(string fullPath, CancellationToken cancellationToken) =>
        this._sklbCache.GetOrAdd(
            fullPath.ToLowerInvariant(),
            async () => {
                var sklb = await this.LoadFileResource<SklbFile>(fullPath, this._disposing.Token);
                return sklb is { LoadException: null, Bones.Length: > 0 } ? sklb : null;
            },
            cancellationToken);
}
