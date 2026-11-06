using StorkDrop.Contracts.Models;
using StorkDrop.Contracts.Services;

namespace StorkDrop.Installer;

public static class MarketplaceChannelSelector
{
    /// <summary>
    /// Picks the channel a product card represents. An installed product keeps showing the channel it
    /// was installed from. Otherwise channels whose version is a plain release win over channels that
    /// carry a pre-release suffix (feature or epic builds), and the highest version is only compared
    /// inside that group, so a feature build with a higher core version never replaces the release
    /// channel.
    /// </summary>
    public static T SelectBest<T>(
        IReadOnlyList<T> channels,
        Func<T, ProductManifest> manifest,
        Func<T, string> feedId,
        string? installedFeedId
    )
    {
        if (!string.IsNullOrEmpty(installedFeedId))
        {
            List<T> installedChannel = channels
                .Where(c => string.Equals(feedId(c), installedFeedId, StringComparison.Ordinal))
                .ToList();
            if (installedChannel.Count > 0)
                return HighestVersion(installedChannel, manifest);
        }

        List<T> releases = channels.Where(c => !IsPreRelease(manifest(c).Version)).ToList();
        return HighestVersion(releases.Count > 0 ? releases : channels, manifest);
    }

    private static T HighestVersion<T>(
        IReadOnlyList<T> channels,
        Func<T, ProductManifest> manifest
    ) => channels.OrderByDescending(c => manifest(c).Version, VersionComparer.Instance).First();

    private static bool IsPreRelease(string version)
    {
        int metadata = version.IndexOf('+', StringComparison.Ordinal);
        string withoutMetadata = metadata >= 0 ? version[..metadata] : version;
        return withoutMetadata.Contains('-', StringComparison.Ordinal);
    }
}
