namespace StorkDrop.Contracts.Services;

/// <summary>
/// White-label identity for a StorkDrop installation. An unbranded install uses <see cref="Default"/>.
/// The <see cref="Prefix"/> drives every per-installation path (see <see cref="StorkPaths"/>) so that
/// branded editions are fully isolated from each other and from vanilla StorkDrop.
/// </summary>
public sealed record BrandingInfo(
    string? Prefix = null,
    string? DisplayName = null,
    string? LogoPath = null,
    bool ForbidNewFeeds = false,
    BrandingFeed? Feed = null,
    string[]? VisibleChannels = null
)
{
    public static BrandingInfo Default { get; } = new BrandingInfo();

    /// <summary>True when any visual white-label field is set, i.e. this is not a plain StorkDrop install.</summary>
    public bool IsBranded =>
        !string.IsNullOrWhiteSpace(Prefix)
        || !string.IsNullOrWhiteSpace(DisplayName)
        || !string.IsNullOrWhiteSpace(LogoPath);

    /// <summary>True when the edition pre-configures a primary feed whose identity is vendor-fixed.</summary>
    public bool HasFeed =>
        Feed is not null
        && (
            !string.IsNullOrWhiteSpace(Feed.Name)
            || !string.IsNullOrWhiteSpace(Feed.Url)
            || (
                Feed.Provider == Models.FeedProvider.S3
                && !string.IsNullOrWhiteSpace(Feed.S3?.Bucket)
            )
        );

    /// <summary>The install/config/data folder name, e.g. "acme-StorkDrop" or plain "StorkDrop".</summary>
    public string AppFolderName =>
        string.IsNullOrWhiteSpace(Prefix) ? "StorkDrop" : $"{Prefix}-StorkDrop";

    /// <summary>The window title, e.g. "StorkDrop - Acme GmbH Edition" or plain "StorkDrop".</summary>
    public string WindowTitle =>
        string.IsNullOrWhiteSpace(DisplayName) ? "StorkDrop" : $"StorkDrop - {DisplayName}";
}
