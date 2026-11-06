using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public static class UpdateDetector
{
    /// <summary>
    /// Matches every installed instance against the products of the feed it was installed from, so a
    /// version offered on a different channel is never reported as an update. Instances whose feed
    /// could not be read are left out.
    /// </summary>
    public static IReadOnlyList<AvailableUpdate> FindUpdates(
        IReadOnlyList<InstalledProduct> installed,
        IReadOnlyDictionary<string, IReadOnlyList<ProductManifest>> productsByFeed
    )
    {
        List<AvailableUpdate> updates = [];
        foreach (InstalledProduct product in installed)
        {
            if (
                string.IsNullOrEmpty(product.FeedId)
                || !productsByFeed.TryGetValue(
                    product.FeedId,
                    out IReadOnlyList<ProductManifest>? feedProducts
                )
            )
                continue;

            ProductManifest? latest = feedProducts.FirstOrDefault(m =>
                string.Equals(m.ProductId, product.ProductId, StringComparison.Ordinal)
            );
            if (
                latest is not null
                && !string.IsNullOrEmpty(latest.Version)
                && VersionComparer.IsNewer(latest.Version, product.Version)
            )
                updates.Add(new AvailableUpdate(product, latest));
        }

        return updates;
    }
}
