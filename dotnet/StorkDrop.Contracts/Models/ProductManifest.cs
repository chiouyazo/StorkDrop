namespace StorkDrop.Contracts.Models;

/// <summary>
/// Describes a product available for installation, including metadata, plugin info, and shortcuts.
/// </summary>
public sealed record ProductManifest(
    string ProductId,
    string Title,
    string Version,
    DateOnly ReleaseDate,
    InstallType InstallType,
    string? Description = null,
    string? ReleaseNotes = null,
    string? RecommendedInstallPath = null,
    string[]? Requirements = null,
    CleanupInfo? CleanupInfo = null,
    string[]? BundledProductIds = null,
    StorkPluginInfo[]? Plugins = null,
    ShortcutInfo[]? Shortcuts = null,
    EnvironmentVariableInfo[]? EnvironmentVariables = null,
    string? ImageUrl = null,
    string? Publisher = null,
    long? DownloadSizeBytes = null,
    string? ShortcutFolder = null,
    string[]? RequiredProductIds = null,
    OptionalPostProduct[]? OptionalPostProducts = null,
    string? BadgeText = null,
    string? BadgeColor = null,
    string[]? PreserveOnSwitch = null,
    bool AllowMultipleInstances = false
);
