using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Interfaces;

/// <summary>
/// Manages all configured feeds and provides per-feed IRegistryClient instances.
/// </summary>
public interface IFeedRegistry
{
    /// <summary>
    /// Gets metadata for all currently configured feeds.
    /// </summary>
    IReadOnlyList<FeedInfo> GetFeeds();

    /// <summary>
    /// Gets the IRegistryClient for the specified feed ID.
    /// Throws KeyNotFoundException if the feed ID is unknown.
    /// </summary>
    IRegistryClient GetClient(string feedId);

    /// <summary>
    /// Tests connectivity for a specific feed.
    /// </summary>
    Task<bool> TestConnectionAsync(string feedId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads all feed configurations from the configuration service.
    /// Call this after settings are saved or at startup.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
