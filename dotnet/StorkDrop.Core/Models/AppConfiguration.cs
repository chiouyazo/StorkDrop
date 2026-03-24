namespace StorkDrop.Core.Models;

/// <summary>
/// Represents the application-wide configuration including feeds, proxy, and behavior settings.
/// </summary>
public sealed record AppConfiguration(
    FeedConfiguration[] Feeds,
    bool AutoStart,
    bool AutoCheckForUpdates,
    TimeSpan CheckInterval,
    ProxySettings? ProxySettings = null,
    string Language = "en",
    string? LogLevel = "Information",
    bool HasShownTrayToast = false
);
