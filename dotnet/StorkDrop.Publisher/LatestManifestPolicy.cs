using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Publisher;

/// <summary>Decides whether a published version may replace the channel's root "latest" manifest.</summary>
public static class LatestManifestPolicy
{
    public static bool ShouldReplace(
        ProductManifest? currentLatest,
        ProductManifest published,
        bool force
    )
    {
        if (force || currentLatest is null)
            return true;

        return VersionComparer.Instance.Compare(published.Version, currentLatest.Version) >= 0;
    }
}
