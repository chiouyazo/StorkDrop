namespace StorkDrop.Contracts.Models;

/// <summary>
/// Represents a channel (feed) that provides a specific product.
/// Channels correspond to badge variants like STABLE, DEV, or FEATURE.
/// </summary>
public sealed record ChannelInfo(
    string FeedId,
    string FeedName,
    string? BadgeText,
    string? BadgeColor,
    string LatestVersion
)
{
    /// <summary>
    /// Gets a display-friendly name for the channel, preferring the badge text over the feed name.
    /// </summary>
    public string DisplayName => BadgeText ?? FeedName;
}
