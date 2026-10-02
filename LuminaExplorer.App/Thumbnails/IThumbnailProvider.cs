using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>Creates thumbnails for a set of file types.</summary>
/// <remarks>Implementations must be thread-safe; <see cref="CreateAsync"/> is called from multiple worker threads.
/// </remarks>
public interface IThumbnailProvider {
    /// <summary>Gets the priority of this provider. Providers with a higher priority are tried first.</summary>
    public int Priority { get; }

    /// <summary>Gets how expensive creating a thumbnail with this provider is.</summary>
    public ThumbnailCost Cost { get; }

    /// <summary>Tests whether this provider may be able to create a thumbnail for the request.</summary>
    /// <remarks>Must be cheap; only look at the properties of <paramref name="request"/>.</remarks>
    public bool CanHandle(ThumbnailRequest request);

    /// <summary>Creates a thumbnail.</summary>
    /// <returns>The thumbnail, or null if this provider turned out to be unable to handle the file, in which case the
    /// next provider is tried.</returns>
    public Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken);
}

public enum ThumbnailCost {
    /// <summary>Reads a small part of the file, or a small file.</summary>
    Cheap,

    /// <summary>Reads and parses the whole file, and possibly other files.</summary>
    Medium,

    /// <summary>Renders using the GPU. Not implemented yet.</summary>
    Gpu,
}

public enum ThumbnailKind {
    /// <summary>The content of the file itself, such as a texture.</summary>
    Image,

    /// <summary>A card showing information about the file.</summary>
    InfoCard,

    /// <summary>A visualization of the content of the file, such as a skeleton.</summary>
    Render,

    /// <summary>The icon associated with the file type by the shell.</summary>
    AssociationIcon,
}

public sealed class ThumbnailResult : IDisposable {
    public ThumbnailResult(Bitmap bitmap, ThumbnailKind kind)
    {
        this.Bitmap = bitmap;
        this.Kind = kind;
    }

    public Bitmap Bitmap { get; }

    public ThumbnailKind Kind { get; }

    public void Dispose() => this.Bitmap.Dispose();
}
