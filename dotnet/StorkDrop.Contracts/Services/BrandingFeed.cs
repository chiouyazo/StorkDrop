using StorkDrop.Contracts.Models;

namespace StorkDrop.Contracts.Services;

/// <summary>
/// A feed pre-configured by a white-label edition. Identity (name, URL for Nexus, or S3 coordinates)
/// is fixed by the vendor and locked in the UI; the user still supplies credentials. The optional
/// pre-hashed lock password gates install/update/uninstall for the feed.
/// </summary>
public sealed record BrandingFeed(
    string? Name = null,
    string? Url = null,
    string? LockPasswordHash = null,
    FeedProvider Provider = FeedProvider.Nexus,
    BrandingS3? S3 = null
);
