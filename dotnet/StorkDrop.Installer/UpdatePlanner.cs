using StorkDrop.Contracts.Interfaces;
using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public static class UpdatePlanner
{
    /// <summary>
    /// Decides what a headless update of <paramref name="installed"/> may do. The update is read from
    /// the feed the product was installed from; another feed is only used when
    /// <paramref name="explicitFeedId"/> asks for it, and an older version only when
    /// <paramref name="allowDowngrade"/> is set. Feed errors surface instead of silently routing the
    /// update to another feed.
    /// </summary>
    public static async Task<UpdatePlan> PlanAsync(
        IFeedRegistry feedRegistry,
        InstalledProduct installed,
        string? explicitVersion,
        string? explicitFeedId,
        bool allowDowngrade,
        CancellationToken cancellationToken = default
    )
    {
        string? feedId = !string.IsNullOrEmpty(explicitFeedId) ? explicitFeedId : installed.FeedId;
        (ProductManifest? manifest, string? usedFeedId) = string.IsNullOrEmpty(feedId)
            ? await FindInAnyFeedAsync(
                feedRegistry,
                installed.ProductId,
                explicitVersion,
                cancellationToken
            )
            : (
                await GetManifestAsync(
                    feedRegistry.GetClient(feedId),
                    installed.ProductId,
                    explicitVersion,
                    cancellationToken
                ),
                feedId
            );

        if (manifest is null || usedFeedId is null)
            return new UpdatePlan(UpdateDecision.ManifestNotFound, null, usedFeedId);

        int comparison = VersionComparer.Instance.Compare(manifest.Version, installed.Version);
        if (explicitVersion is null)
            return new UpdatePlan(
                comparison > 0 ? UpdateDecision.Proceed : UpdateDecision.AlreadyUpToDate,
                manifest,
                usedFeedId
            );

        return new UpdatePlan(
            comparison < 0 && !allowDowngrade
                ? UpdateDecision.DowngradeBlocked
                : UpdateDecision.Proceed,
            manifest,
            usedFeedId
        );
    }

    private static Task<ProductManifest?> GetManifestAsync(
        IRegistryClient client,
        string productId,
        string? version,
        CancellationToken cancellationToken
    ) =>
        version is null
            ? client.GetProductManifestAsync(productId, cancellationToken)
            : client.GetProductManifestAsync(productId, version, cancellationToken);

    private static async Task<(ProductManifest?, string?)> FindInAnyFeedAsync(
        IFeedRegistry feedRegistry,
        string productId,
        string? version,
        CancellationToken cancellationToken
    )
    {
        foreach (FeedInfo feed in feedRegistry.GetFeeds())
        {
            ProductManifest? manifest = await GetManifestAsync(
                feedRegistry.GetClient(feed.Id),
                productId,
                version,
                cancellationToken
            );
            if (manifest is not null)
                return (manifest, feed.Id);
        }

        return (null, null);
    }
}
