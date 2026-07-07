namespace StorkDrop.Contracts.Services;

/// <summary>
/// A feed pre-configured by a white-label edition. Name and URL are fixed by the vendor and locked
/// in the UI (the user still supplies credentials). The optional pre-hashed lock password gates
/// install/update/uninstall for the feed.
/// </summary>
public sealed record BrandingFeed(
    string? Name = null,
    string? Url = null,
    string? LockPasswordHash = null
);
