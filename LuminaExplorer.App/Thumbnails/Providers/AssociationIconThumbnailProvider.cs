using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.Core.VirtualFileSystem.Physical;

namespace LuminaExplorer.App.Thumbnails.Providers;

/// <summary>Falls back to the icon associated with the file type by the shell, for files on the disk.</summary>
public sealed class AssociationIconThumbnailProvider : IThumbnailProvider {
    public int Priority => -100;

    public ThumbnailCost Cost => ThumbnailCost.Cheap;

    public bool CanHandle(ThumbnailRequest request) => request.File is PhysicalFile;

    public Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        if (request.File is not PhysicalFile pf)
            return Task.FromResult<ThumbnailResult?>(null);

        using var icon = Icon.ExtractAssociatedIcon(pf.FileInfo.FullName);
        return Task.FromResult(icon is null ? null : new ThumbnailResult(icon.ToBitmap(), ThumbnailKind.AssociationIcon));
    }
}
