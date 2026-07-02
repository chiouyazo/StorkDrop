using System.Linq;
using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// Helpers for the optional per-feed operation lock. Maps a runtime feed id (which may
/// be a composite "baseId:repoName" produced by discovery mode) back to the configured
/// <see cref="FeedConfiguration"/> that carries the lock, and verifies entered passwords.
/// </summary>
public static class FeedLock
{
    /// <summary>
    /// Resolves a runtime feed id to its configured feed. Handles composite discovery ids
    /// of the form "baseId:repoName" by matching on the base id. Returns null when unknown.
    /// </summary>
    public static FeedConfiguration? ResolveFeed(
        IEnumerable<FeedConfiguration> feeds,
        string? feedId
    )
    {
        if (string.IsNullOrEmpty(feedId) || feeds is null)
            return null;

        FeedConfiguration[] all = feeds as FeedConfiguration[] ?? feeds.ToArray();

        // Exact match first (pinned mode, or the base id used directly).
        FeedConfiguration? exact = all.FirstOrDefault(f =>
            string.Equals(f.Id, feedId, StringComparison.Ordinal)
        );
        if (exact is not null)
            return exact;

        // Composite discovery id "baseId:repoName" -> match the base configuration.
        int separator = feedId.IndexOf(':');
        if (separator > 0)
        {
            string baseId = feedId[..separator];
            return all.FirstOrDefault(f => string.Equals(f.Id, baseId, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>Whether the given configured feed has a lock password set.</summary>
    public static bool IsLocked(FeedConfiguration? feed) =>
        !string.IsNullOrEmpty(feed?.LockPasswordHash);

    /// <summary>Whether the feed identified by <paramref name="feedId"/> is locked.</summary>
    public static bool IsLocked(IEnumerable<FeedConfiguration> feeds, string? feedId) =>
        IsLocked(ResolveFeed(feeds, feedId));

    /// <summary>Verifies an entered password against the feed's stored lock hash.</summary>
    public static bool Verify(FeedConfiguration feed, string? password) =>
        PasswordHasher.Verify(password, feed.LockPasswordHash);
}
