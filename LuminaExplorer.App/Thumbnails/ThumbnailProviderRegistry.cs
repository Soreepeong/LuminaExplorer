using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LuminaExplorer.App.Thumbnails.Providers;

namespace LuminaExplorer.App.Thumbnails;

/// <summary>Picks thumbnail providers for requests, in the order of their priorities.</summary>
public sealed class ThumbnailProviderRegistry {
    private readonly IThumbnailProvider[] _providers;

    public ThumbnailProviderRegistry(IEnumerable<IThumbnailProvider> providers)
    {
        // Stable sort, so that providers with a same priority are tried in the order of registration.
        this._providers = providers
            .Select((x, i) => (Provider: x, Index: i))
            .OrderByDescending(x => x.Provider.Priority)
            .ThenBy(x => x.Index)
            .Select(x => x.Provider)
            .ToArray();
    }

    /// <summary>Gets a registry with all built-in providers.</summary>
    public static ThumbnailProviderRegistry Default { get; } = new(
    [
        new HwcThumbnailProvider(),
        new SklbThumbnailProvider(),
        new AvfxThumbnailProvider(),
        new ExhThumbnailProvider(),
        new ExdThumbnailProvider(),
        new ShpkThumbnailProvider(),
        new ShcdThumbnailProvider(),
        new ScdThumbnailProvider(),
        new TexThumbnailProvider(),
        new AssociationIconThumbnailProvider(),
    ]);

    public IReadOnlyList<IThumbnailProvider> Providers => this._providers;

    /// <summary>Gets the maximum time a provider of the given cost may take, before the next provider is tried.
    /// </summary>
    public static TimeSpan GetTimeout(ThumbnailCost cost) => cost switch {
        ThumbnailCost.Cheap => TimeSpan.FromSeconds(10),
        ThumbnailCost.Medium => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60),
    };

    public bool CanHandle(ThumbnailRequest request) => this._providers.Any(x => x.CanHandle(request));

    /// <summary>Creates a thumbnail using the first provider that succeeds.</summary>
    /// <returns>The thumbnail, or null if no provider could create one.</returns>
    /// <exception cref="OperationCanceledException">If <paramref name="cancellationToken"/> is cancelled.</exception>
    public async Task<ThumbnailResult?> CreateAsync(
        ThumbnailRequest request,
        ThumbnailContext context,
        CancellationToken cancellationToken)
    {
        foreach (var provider in this._providers) {
            cancellationToken.ThrowIfCancellationRequested();
            if (provider.Cost == ThumbnailCost.Gpu || !provider.CanHandle(request))
                continue;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(GetTimeout(provider.Cost));

            Task<ThumbnailResult?> task;
            try {
                task = provider.CreateAsync(request, context, timeout.Token);
            } catch (Exception e) {
                Debug.WriteLine($"Thumbnail provider {provider.GetType().Name} failed for {request.Name}: {e}");
                continue;
            }

            try {
                // Do not trust providers to honor the cancellation token.
                if (await task.WaitAsync(timeout.Token) is { } result)
                    return result;
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                DisposeResultWhenDone(task);
                throw;
            } catch (OperationCanceledException) {
                Debug.WriteLine($"Thumbnail provider {provider.GetType().Name} timed out for {request.Name}");
                DisposeResultWhenDone(task);
            } catch (Exception e) {
                Debug.WriteLine($"Thumbnail provider {provider.GetType().Name} failed for {request.Name}: {e}");
            }
        }

        return null;
    }

    private static void DisposeResultWhenDone(Task<ThumbnailResult?> task) =>
        _ = task.ContinueWith(
            r => {
                if (r.IsCompletedSuccessfully)
                    r.Result?.Dispose();
            },
            TaskScheduler.Default);
}
